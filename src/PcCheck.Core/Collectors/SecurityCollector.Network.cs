using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using PcCheck.Core.Analysis;
using PcCheck.Core.Models;

namespace PcCheck.Core.Collectors;

public static partial class SecurityCollector
{
    // ===== Kết nối TCP (GetExtendedTcpTable: kèm PID tiến trình sở hữu) =====

    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int TcpStateListen = 2;
    private const int TcpStateEstablished = 5;

#pragma warning disable SYSLIB1054
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
#pragma warning restore SYSLIB1054

    private static List<ConnectionEntry> ReadConnections()
    {
        var list = new List<ConnectionEntry>();
        ReadTcpTable(AfInet, list);
        ReadTcpTable(AfInet6, list);
        return list;
    }

    private static void ReadTcpTable(int family, List<ConnectionEntry> list)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidAll, 0);
        if (size <= 0) return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            size += 4096;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var result = GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidAll, 0);
                if (result == 122) continue; // ERROR_INSUFFICIENT_BUFFER: bảng vừa lớn thêm, thử lại.
                if (result != 0) return;

                var count = Marshal.ReadInt32(buffer);
                var row = buffer + 4;
                for (var i = 0; i < count; i++)
                {
                    if (family == AfInet)
                    {
                        // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid (6 × uint32).
                        var state = Marshal.ReadInt32(row);
                        var local = new IPAddress((uint)Marshal.ReadInt32(row, 4));
                        var localPort = Port(Marshal.ReadInt32(row, 8));
                        var remote = new IPAddress((uint)Marshal.ReadInt32(row, 12));
                        var remotePort = Port(Marshal.ReadInt32(row, 16));
                        var pid = Marshal.ReadInt32(row, 20);
                        Add(list, state, pid, local, localPort, remote, remotePort, false);
                        row += 24;
                    }
                    else
                    {
                        // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, pid.
                        var localBytes = new byte[16];
                        Marshal.Copy(row, localBytes, 0, 16);
                        var localPort = Port(Marshal.ReadInt32(row, 20));
                        var remoteBytes = new byte[16];
                        Marshal.Copy(row + 24, remoteBytes, 0, 16);
                        var remotePort = Port(Marshal.ReadInt32(row, 44));
                        var state = Marshal.ReadInt32(row, 48);
                        var pid = Marshal.ReadInt32(row, 52);
                        Add(list, state, pid, new IPAddress(localBytes), localPort, new IPAddress(remoteBytes), remotePort, true);
                        row += 56;
                    }
                }

                return;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static void Add(List<ConnectionEntry> list, int state, int pid, IPAddress local, int localPort, IPAddress remote, int remotePort, bool v6)
    {
        if (state is not (TcpStateListen or TcpStateEstablished) || pid <= 4) return;
        list.Add(new ConnectionEntry(pid, local.ToString(), localPort, remote.ToString(), remotePort, state == TcpStateListen, v6));
    }

    /// <summary>Cổng ở byte thấp của DWORD theo thứ tự byte mạng.</summary>
    private static int Port(int value) => ((value & 0xFF) << 8) | ((value >> 8) & 0xFF);

    // ===== Hosts / proxy =====

    private static List<HostsEntry> ReadHosts()
    {
        var path = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
        return File.Exists(path) ? SecurityCatalog.ParseHosts(File.ReadLines(path)) : [];
    }

    private static ProxyInfo ReadProxy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        return new ProxyInfo
        {
            Enabled = key?.GetValue("ProxyEnable") is int e && e != 0,
            Server = key?.GetValue("ProxyServer") as string ?? "",
            AutoConfigUrl = key?.GetValue("AutoConfigURL") as string ?? "",
        };
    }

    // ===== Chứng chỉ gốc được thêm thủ công =====

    /// <summary>
    /// Chứng chỉ trong kho "Trusted Root" nhưng không có trong kho AuthRoot (các CA Windows tự cập nhật) – tức là do phần mềm
    /// hoặc người dùng tự thêm. Chứng chỉ gốc lạ cho phép giải mã lưu lượng HTTPS (nghe lén mật khẩu, tin nhắn).
    /// </summary>
    private static List<CertificateEntry> ReadAddedRootCertificates()
    {
        var authRoot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var store = new X509Store("AuthRoot", StoreLocation.LocalMachine))
        {
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var cert in store.Certificates)
            {
                authRoot.Add(cert.Thumbprint);
                cert.Dispose();
            }
        }

        var list = new List<CertificateEntry>();
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var store = new X509Store(StoreName.Root, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var cert in store.Certificates)
            {
                using (cert)
                {
                    if (authRoot.Contains(cert.Thumbprint)) continue;
                    if (list.Any(c => c.Thumbprint == cert.Thumbprint)) continue;

                    // Các gốc cài sẵn của Windows không nằm trong AuthRoot (Microsoft Root Authority, chứng chỉ timestamp cũ...).
                    if (cert.Subject.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) && cert.Issuer == cert.Subject) continue;
                    if (cert.Subject.Contains("Time Stamping", StringComparison.OrdinalIgnoreCase) || cert.Subject.Contains("Copyright (c) 1997", StringComparison.OrdinalIgnoreCase)) continue;

                    list.Add(new CertificateEntry(
                        cert.GetNameInfo(X509NameType.SimpleName, false) is { Length: > 0 } n ? n : cert.Subject,
                        cert.GetNameInfo(X509NameType.SimpleName, true) is { Length: > 0 } i ? i : cert.Issuer,
                        cert.Thumbprint,
                        cert.NotAfter,
                        location == StoreLocation.LocalMachine ? "LocalMachine" : "CurrentUser"));
                }
            }
        }

        return list;
    }
}

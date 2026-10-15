using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using PcCheck.Core.Fixes;
using PcCheck.Core.Util;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Sensors;

/// <summary>
/// Cài driver PawnIO (mã nguồn mở, có chữ ký số) để đọc nhiệt độ CPU chính xác – người dùng chỉ cần bấm 1 nút.
/// Tải bản chính thức từ GitHub qua HTTPS vào thư mục chỉ admin ghi được, khóa tệp, kiểm tra chữ ký Authenticode
/// rồi mới chạy "-install -silent" – tiến trình thường không thể tráo tệp giữa lúc kiểm tra và lúc chạy.
/// </summary>
public static class PawnIoInstaller
{
    public const string DownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
    public const string HomePage = "https://pawnio.eu/";

    private const long MaxDownloadBytes = 30L * 1024 * 1024;
    private const int ErrorSuccessRebootRequired = 3010;
    private const int ErrorCancelled = 1223;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(3);

    private static readonly object Sync = new();
    private static Process? _running;

    /// <summary>Trình cài đặt từ lần trước vẫn đang chạy (vd đã quá thời gian chờ nhưng chưa thoát).</summary>
    public static bool IsRunning
    {
        get
        {
            lock (Sync)
            {
                try
                {
                    return _running is { HasExited: false };
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }
    }

    public static async Task<FixOutcome> InstallAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (IsRunning) return new FixOutcome(false, L("cpu_sensor.busy"));

        var elevated = Elevation.IsAdministrator();
        string dir;
        try
        {
            dir = CreateWorkDirectory(elevated);
        }
        catch (Exception ex)
        {
            Log.Error("Không tạo được thư mục tạm cho PawnIO", ex);
            return new FixOutcome(false, L("cpu_sensor.result.failed", ex.Message));
        }

        var path = Path.Combine(dir, "PawnIO_setup.exe");
        var keepFiles = false;
        try
        {
            progress?.Report("download");
            if (!await DownloadAsync(path, ct).ConfigureAwait(false)) return new FixOutcome(false, L("cpu_sensor.result.download_failed"));

            // Giữ khóa đọc (không cho ghi / xóa / đổi tên) từ lúc kiểm tra chữ ký tới khi tiến trình đã chạy.
            using var fileLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            progress?.Report("verify");
            if (!Authenticode.IsTrusted(path))
            {
                Log.Warn("PawnIO_setup.exe không có chữ ký số hợp lệ – hủy cài đặt");
                return new FixOutcome(false, L("cpu_sensor.result.untrusted"));
            }

            progress?.Report("install");
            // Ứng dụng thường đã chạy với quyền admin; nếu chưa thì để Windows hỏi UAC.
            var process = Process.Start(new ProcessStartInfo(path, "-install -silent")
            {
                UseShellExecute = !elevated,
                Verb = elevated ? "" : "runas",
                CreateNoWindow = true,
                WorkingDirectory = dir,
            });
            if (process is null) return new FixOutcome(false, L("cpu_sensor.result.failed", "-1"));
            lock (Sync) _running = process;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(InstallTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Trình cài vẫn chạy: không xóa tệp, dọn khi nó thoát; chặn lần cài mới cho tới lúc đó.
                Log.Warn("Cài PawnIO quá thời gian chờ – trình cài vẫn đang chạy");
                keepFiles = true;
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => FinishBackground(process, dir);
                if (process.HasExited) FinishBackground(process, dir);
                return new FixOutcome(false, L("cpu_sensor.result.timeout"));
            }

            var exitCode = process.ExitCode;
            lock (Sync)
            {
                if (ReferenceEquals(_running, process)) _running = null;
            }

            process.Dispose();
            Log.Info($"PawnIO setup kết thúc với mã {exitCode}");
            return exitCode switch
            {
                0 => new FixOutcome(true, L("cpu_sensor.result.ok")),
                ErrorSuccessRebootRequired => new FixOutcome(true, L("cpu_sensor.result.reboot")),
                ErrorCancelled => new FixOutcome(false, L("cpu_sensor.result.cancelled")),
                var code => new FixOutcome(false, L("cpu_sensor.result.failed", code)),
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // Người dùng bấm "No" ở hộp thoại UAC.
            return new FixOutcome(false, L("cpu_sensor.result.cancelled"));
        }
        catch (Exception ex)
        {
            Log.Error("Cài PawnIO lỗi", ex);
            return new FixOutcome(false, L("cpu_sensor.result.failed", ex.Message));
        }
        finally
        {
            if (!keepFiles) DeleteDirectory(dir);
        }
    }

    /// <summary>Tải tệp cài; thời gian chờ áp dụng cho cả phần thân (HttpClient.Timeout không bao phần đọc luồng).</summary>
    private static async Task<bool> DownloadAsync(string path, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DownloadTimeout);
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PCCheck/1.0");
            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxDownloadBytes) return false;

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxDownloadBytes) return false;
                await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }

            return total >= 10_000;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            Log.Warn("Tải PawnIO lỗi", ex);
            return false;
        }
    }

    /// <summary>
    /// Khi chạy với quyền admin: thư mục riêng trong ProgramData chỉ Administrators / SYSTEM ghi được, để tiến trình
    /// thường của người dùng không thể thay tệp cài trước khi nó chạy với quyền cao. Chưa có quyền admin thì dùng %TEMP%
    /// (Windows sẽ hỏi UAC và hiện nhà phát hành của tệp).
    /// </summary>
    private static string CreateWorkDirectory(bool elevated)
    {
        var name = "pawnio-" + Guid.NewGuid().ToString("N");
        if (!elevated || !OperatingSystem.IsWindows())
        {
            var temp = Path.Combine(Path.GetTempPath(), name);
            Directory.CreateDirectory(temp);
            return temp;
        }

        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PcCheck");
        Directory.CreateDirectory(parent);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        var path = Path.Combine(parent, name);
        new DirectoryInfo(path).Create(security);
        return path;
    }

    private static void FinishBackground(Process process, string dir)
    {
        lock (Sync)
        {
            if (ReferenceEquals(_running, process)) _running = null;
        }

        try
        {
            Log.Info($"PawnIO setup (chạy nền) kết thúc với mã {process.ExitCode}");
        }
        catch (InvalidOperationException)
        {
        }

        DeleteDirectory(dir);
    }

    private static void DeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Kiểm tra chữ ký số Authenticode của tệp thực thi bằng WinVerifyTrust (giống Windows khi chạy file tải về).</summary>
internal static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint StateActionIgnore = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

#pragma warning disable SYSLIB1054
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);
#pragma warning restore SYSLIB1054

    public static bool IsTrusted(string filePath)
    {
        var pathPtr = Marshal.StringToHGlobalUni(filePath);
        var filePtr = IntPtr.Zero;
        var dataPtr = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = pathPtr,
            };
            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, filePtr, false);

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = UiNone,
                fdwRevocationChecks = RevokeNone,
                dwUnionChoice = ChoiceFile,
                pFile = filePtr,
                dwStateAction = StateActionIgnore,
            };
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPtr, false);

            var result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataPtr);
            if (result != 0) Log.Warn($"WinVerifyTrust({Path.GetFileName(filePath)}) = 0x{result:X8}");
            return result == 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Kiểm tra chữ ký số lỗi", ex);
            return false;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }
}

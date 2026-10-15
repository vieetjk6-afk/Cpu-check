using System.Diagnostics;
using System.Net.NetworkInformation;

namespace PcCheck.Core.Util;

/// <summary>Tốc độ mạng tải xuống / tải lên (byte/giây) tính từ bộ đếm của các card mạng thật đang hoạt động.</summary>
public sealed class NetworkMonitor
{
    private static readonly string[] VirtualHints =
        ["Hyper-V", "VMware", "VirtualBox", "Virtual Ethernet", "Loopback", "WAN Miniport", "Teredo", "isatap", "Bluetooth", "Pseudo"];

    private long _lastReceived;
    private long _lastSent;
    private long _lastTimestamp;

    public double? DownloadBytesPerSecond { get; private set; }

    public double? UploadBytesPerSecond { get; private set; }

    /// <summary>Lấy mẫu mới; lần gọi đầu chỉ khởi tạo bộ đếm nên tốc độ vẫn là null.</summary>
    public void Sample()
    {
        long received = 0, sent = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsCounted(nic)) continue;
                try
                {
                    var stats = nic.GetIPStatistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
                catch (NetworkInformationException)
                {
                }
            }
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn("Đọc thống kê mạng lỗi", ex);
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_lastTimestamp != 0)
        {
            var seconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            if (seconds > 0.05)
            {
                DownloadBytesPerSecond = Math.Max(0, (received - _lastReceived) / seconds);
                UploadBytesPerSecond = Math.Max(0, (sent - _lastSent) / seconds);
            }
        }

        _lastReceived = received;
        _lastSent = sent;
        _lastTimestamp = now;
    }

    internal static bool IsCounted(NetworkInterface nic) =>
        nic.OperationalStatus == OperationalStatus.Up
        && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        && !VirtualHints.Any(h => nic.Description.Contains(h, StringComparison.OrdinalIgnoreCase));
}

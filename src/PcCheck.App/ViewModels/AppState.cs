using PcCheck.Core.Analysis;
using PcCheck.Core.Models;
using PcCheck.Core.Sensors;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

/// <summary>Trạng thái dùng chung giữa các trang: kết quả quét gần nhất, cảm biến trực tiếp, cài đặt.</summary>
public sealed class AppState : IDisposable
{
    public AppState(AppSettings settings) => Settings = settings;

    public AppSettings Settings { get; }

    public SensorService Sensors { get; } = new();

    public SystemMetrics Metrics { get; } = new();

    /// <summary>Đang quét toàn bộ – các thao tác quét một phần phải chờ.</summary>
    public bool IsScanning { get; set; }

    public ScanResult? Result { get; private set; }

    public SensorSnapshot Live { get; private set; } = SensorSnapshot.Empty;

    public double? LiveCpuLoad { get; private set; }

    public long MemoryTotal { get; private set; }

    public long MemoryAvailable { get; private set; }

    /// <summary>Kết quả quét thay đổi (luôn phát trên luồng giao diện).</summary>
    public event EventHandler? ResultChanged;

    /// <summary>Có số liệu cảm biến mới (khoảng 2 giây/lần).</summary>
    public event EventHandler? LiveUpdated;

    public void SetResult(ScanResult result)
    {
        Result = result;
        ResultChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cập nhật một phần dữ liệu (vd quét lại driver, dọn rác) rồi phân tích lại mà không cần quét toàn bộ.</summary>
    public async Task ReanalyzeAsync(Func<ScanData, Task> mutate)
    {
        if (Result is not { } original) return;
        var data = original.Data;
        await mutate(data);
        var result = await Task.Run(() => Analyzer.Analyze(data, DateTime.Now, original.Duration));

        // Một lần quét toàn bộ mới hơn đã hoàn tất trong lúc chờ -> không ghi đè bằng dữ liệu cũ.
        if (ReferenceEquals(Result, original)) SetResult(result);
    }

    public async Task RefreshLiveAsync()
    {
        var snapshot = await Sensors.RefreshAsync();
        var (total, available) = SystemMetrics.Memory();
        Live = snapshot;
        LiveCpuLoad = snapshot.CpuLoad ?? Metrics.CpuUsage();
        MemoryTotal = total;
        MemoryAvailable = available;
        LiveUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Sensors.Dispose();
}

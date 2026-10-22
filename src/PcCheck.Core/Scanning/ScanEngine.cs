using System.Diagnostics;
using PcCheck.Core.Analysis;
using PcCheck.Core.Cleanup;
using PcCheck.Core.Collectors;
using PcCheck.Core.Models;
using PcCheck.Core.Sensors;
using PcCheck.Core.Util;

namespace PcCheck.Core.Scanning;

/// <summary>Chạy song song tất cả bộ thu thập rồi phân tích. Một bước lỗi không làm hỏng cả lần quét.</summary>
public sealed class ScanEngine(SensorService sensors)
{
    public async Task<ScanResult> RunAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var data = new ScanData();

        (string Key, Func<Task> Work)[] steps =
        [
            ("scan.step.hardware", () => Task.Run(() => data.Hardware = HardwareCollector.Collect(), ct)),
            ("scan.step.sensors", async () => data.Sensors = await sensors.RefreshAsync(ct).ConfigureAwait(false)),
            ("scan.step.drivers", () => Task.Run(() => data.Devices = DriverCollector.Collect(), ct)),
            ("scan.step.events", () => Task.Run(() => data.Events = EventLogCollector.Collect(), ct)),
            ("scan.step.system", () => Task.Run(() => data.System = SystemStatusCollector.Collect(), ct)),
            ("scan.step.junk", async () => data.Junk = await JunkScanner.ScanAsync(ct).ConfigureAwait(false)),
            ("scan.step.security", () => Task.Run(() => data.Security = SecurityCollector.Collect(), ct)),
            ("scan.step.software", () => Task.Run(() =>
            {
                data.Programs = SoftwareCollector.Collect();
                data.Startup = StartupCollector.Collect();
            }, ct)),
        ];

        var completed = 0;
        progress?.Report(new ScanProgress(0, steps.Length, "scan.step.start"));

        await Task.WhenAll(steps.Select(async step =>
        {
            var stepWatch = Stopwatch.StartNew();
            try
            {
                await step.Work().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"Bước quét {step.Key} lỗi", ex);
            }

            Log.Info($"{step.Key}: {stepWatch.ElapsedMilliseconds} ms");
            var done = Interlocked.Increment(ref completed);
            progress?.Report(new ScanProgress(done, steps.Length, step.Key));
        })).ConfigureAwait(false);

        var result = Analyzer.Analyze(data, DateTime.Now, watch.Elapsed);
        Log.Info($"Quét xong trong {watch.ElapsedMilliseconds} ms: {result.Issues.Count} vấn đề, điểm {result.Score}");
        return result;
    }
}

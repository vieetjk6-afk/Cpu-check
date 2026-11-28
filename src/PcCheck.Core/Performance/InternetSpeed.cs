using System.Diagnostics;
using System.Net.Http;
using PcCheck.Core.Util;

namespace PcCheck.Core.Performance;

/// <summary>
/// Đo tốc độ Internet bằng máy chủ đo tốc độ công khai của Cloudflare (speed.cloudflare.com): độ trễ, tải xuống, tải lên.
/// Chỉ chạy khi người dùng chọn; không gửi thông tin nào ngoài dữ liệu thử ngẫu nhiên.
/// </summary>
public static class InternetSpeed
{
    private const string Host = "https://speed.cloudflare.com";

    public static async Task RunAsync(BenchmarkResult result, Action<string> report, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PCCheck-SpeedTest/1.0");
        try
        {
            report("net_ping");
            result.InternetPingMs = await PingAsync(http, ct).ConfigureAwait(false);
            report("net_down");
            result.InternetDownMbps = await DownloadAsync(http, ct).ConfigureAwait(false);
            report("net_up");
            result.InternetUpMbps = await UploadAsync(http, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Warn("Đo tốc độ Internet lỗi", ex);
            result.InternetError = "bench.error.internet";
        }
    }

    /// <summary>Thời gian phản hồi nhỏ nhất của 6 yêu cầu rỗng (ms).</summary>
    private static async Task<double> PingAsync(HttpClient http, CancellationToken ct)
    {
        var best = double.MaxValue;
        for (var i = 0; i < 6; i++)
        {
            var sw = Stopwatch.StartNew();
            using var response = await http.GetAsync($"{Host}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            sw.Stop();
            if (i > 0) best = Math.Min(best, sw.Elapsed.TotalMilliseconds); // lần đầu có thêm thời gian bắt tay TLS
        }

        return best;
    }

    /// <summary>4 luồng tải song song trong tối đa 8 giây – Mbps.</summary>
    private static async Task<double> DownloadAsync(HttpClient http, CancellationToken ct)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(8));
        long total = 0;
        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (!window.IsCancellationRequested)
                {
                    using var response = await http.GetAsync($"{Host}/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, window.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(window.Token).ConfigureAwait(false);
                    int read;
                    while ((read = await stream.ReadAsync(buffer, window.Token).ConfigureAwait(false)) > 0) Interlocked.Add(ref total, read);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return Interlocked.Read(ref total) * 8 / sw.Elapsed.TotalSeconds / 1e6;
    }

    /// <summary>Tải lên các khối 5 MB trên 3 luồng trong tối đa 8 giây – Mbps (chỉ tính khối đã gửi xong).</summary>
    private static async Task<double> UploadAsync(HttpClient http, CancellationToken ct)
    {
        var payload = new byte[5 * 1000 * 1000];
        Random.Shared.NextBytes(payload);
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(8));
        long total = 0;
        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, 3).Select(async _ =>
        {
            try
            {
                while (!window.IsCancellationRequested)
                {
                    using var content = new ByteArrayContent(payload);
                    using var response = await http.PostAsync($"{Host}/__up", content, window.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    Interlocked.Add(ref total, payload.Length);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var seconds = sw.Elapsed.TotalSeconds;
        return total == 0 ? 0 : total * 8 / seconds / 1e6;
    }
}

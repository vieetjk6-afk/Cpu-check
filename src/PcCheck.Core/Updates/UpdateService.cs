using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PcCheck.Core.Util;

namespace PcCheck.Core.Updates;

/// <summary>Tệp đính kèm của một bản phát hành trên GitHub.</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Sha256);

/// <summary>Bản phát hành mới nhất trên GitHub Releases.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Name,
    string Notes,
    string PageUrl,
    DateTimeOffset? Published,
    IReadOnlyList<ReleaseAsset> Assets);

public enum UpdateStatus
{
    UpToDate,
    Available,
    NoRelease,
    Failed,
}

public sealed record UpdateCheckResult(UpdateStatus Status, ReleaseInfo? Release = null, string? Error = null);

/// <summary>Bản cập nhật đã tải và giải nén, sẵn sàng thay thế tệp đang chạy.</summary>
public sealed record PreparedUpdate(string Directory, string ExePath, string ExeSha256);

/// <summary>Bản mới đã nằm ở chỗ exe cũ và đang bị khóa đọc; giữ tới khi tiến trình mới đã chạy.</summary>
public sealed class InstalledUpdate(string currentExe, string oldExe, FileStream fileLock) : IDisposable
{
    private bool _done;

    /// <summary>Không khởi động được bản mới -> trả exe cũ về chỗ cũ.</summary>
    public void Rollback()
    {
        if (_done) return;
        _done = true;
        fileLock.Dispose();
        UpdateService.Restore(currentExe, oldExe);
    }

    public void Dispose()
    {
        _done = true;
        fileLock.Dispose();
    }
}

/// <summary>Tệp tải về hỏng hoặc không khớp mã kiểm tra SHA-256 do GitHub cung cấp.</summary>
public sealed class UpdateIntegrityException(string message) : Exception(message);

/// <summary>
/// Kiểm tra và cài bản mới từ GitHub Releases của dự án. Chỉ gọi API công khai (không gửi thông tin máy).
/// Bản phát hành được CI tạo khi đẩy tag "vX.Y.Z", kèm 2 tệp: PcCheck-portable-win-x64.zip và PcCheck-small-win-x64.zip.
/// </summary>
public static partial class UpdateService
{
    public const string Owner = "vieetjk6-afk";
    public const string Repository = "Cpu-check";
    public const string ExeName = "PcCheck.exe";

    /// <summary>Tham số dòng lệnh khi khởi động lại sau cập nhật: chờ phiên bản cũ thoát thay vì đánh thức nó.</summary>
    public const string AfterUpdateArgument = "--after-update";

    private const long MaxDownloadBytes = 400L * 1024 * 1024;

    public static string ReleasesPage => $"https://github.com/{Owner}/{Repository}/releases";

    private static string LatestApi => $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var http = CreateClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheckResult(UpdateStatus.NoRelease);
            response.EnsureSuccessStatusCode();

            var release = ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            if (release is null) return new UpdateCheckResult(UpdateStatus.NoRelease);
            return new UpdateCheckResult(IsNewer(release.Version, current) ? UpdateStatus.Available : UpdateStatus.UpToDate, release);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            Log.Warn("Kiểm tra cập nhật lỗi", ex);
            return new UpdateCheckResult(UpdateStatus.Failed, Error: ErrorCode(ex));
        }
    }

    internal static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

        var tag = Str(root, "tag_name") ?? "";
        if (ParseVersion(tag) is not { } version) return null;

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in list.EnumerateArray())
            {
                var name = Str(a, "name");
                var url = Str(a, "browser_download_url");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;

                // GitHub ghi mã kiểm tra dạng "sha256:<hex>".
                var digest = Str(a, "digest");
                var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest[7..].ToLowerInvariant()
                    : null;
                assets.Add(new ReleaseAsset(name, url, size, sha is { Length: 64 } ? sha : null));
            }
        }

        DateTimeOffset? published = DateTimeOffset.TryParse(Str(root, "published_at"), out var p) ? p : null;
        var title = Str(root, "name");
        return new ReleaseInfo(
            version,
            tag,
            string.IsNullOrWhiteSpace(title) ? tag : title,
            Str(root, "body") ?? "",
            Str(root, "html_url") ?? ReleasesPage,
            published,
            assets);
    }

    /// <summary>Tag v1.2.3 / 1.2 / v2 / v1.2.3-beta -> 1.2.3 / 1.2.0 / 2.0.0 / 1.2.3. Trả null nếu không phải số phiên bản.</summary>
    public static Version? ParseVersion(string tag)
    {
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var cut = text.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) text = text[..cut];
        if (int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major))
            return new Version(major, 0, 0);
        if (!System.Version.TryParse(text, out var v)) return null;
        return Normalize(v);
    }

    /// <summary>Mã lỗi ngắn để giao diện dịch: rate_limit, timeout, network, invalid hoặc "HTTP 500".</summary>
    internal static string ErrorCode(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } => "rate_limit",
        HttpRequestException { StatusCode: { } code } => $"HTTP {(int)code}",
        HttpRequestException => "network",
        OperationCanceledException => "timeout",
        _ => "invalid",
    };

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static string AssetName(string flavor) => $"PcCheck-{flavor}-win-x64.zip";

    public static ReleaseAsset? PickAsset(ReleaseInfo release, string flavor) =>
        release.Assets.FirstOrDefault(a => a.Name.Equals(AssetName(flavor), StringComparison.OrdinalIgnoreCase));

    /// <summary>Tải tệp zip, kiểm tra SHA-256 (nếu GitHub cung cấp) rồi giải nén PcCheck.exe vào thư mục an toàn.</summary>
    public static async Task<PreparedUpdate> DownloadAsync(ReleaseAsset asset, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = SecureTemp.CreateDirectory("update");
        try
        {
            var zip = Path.Combine(dir, "PcCheck-update.zip");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(15));
                using var http = CreateClient();
                using var response = await http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? asset.Size;
                if (total > MaxDownloadBytes) throw new UpdateIntegrityException("too large");

                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                await using var target = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[128 * 1024];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    done += read;
                    if (done > MaxDownloadBytes) throw new UpdateIntegrityException("too large");
                    await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    if (total > 0) progress?.Report(Math.Min(100, 100.0 * done / total));
                }
            }

            if (asset.Sha256 is not null && !string.Equals(Sha256Of(zip), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateIntegrityException("sha256 mismatch");

            var exe = ExtractExecutable(zip, Path.Combine(dir, "new"));
            File.Delete(zip);
            return new PreparedUpdate(dir, exe, Sha256Of(exe));
        }
        catch
        {
            SecureTemp.Delete(dir);
            throw;
        }
    }

    /// <summary>Chỉ giải nén đúng tệp PcCheck.exe (không tin đường dẫn trong zip).</summary>
    internal static string ExtractExecutable(string zipPath, string targetDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries
            .Where(e => e.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName.Count(ch => ch is '/' or '\\'))
            .FirstOrDefault() ?? throw new UpdateIntegrityException("PcCheck.exe not found in archive");
        if (entry.Length is < 100_000 or > MaxDownloadBytes) throw new UpdateIntegrityException("unexpected exe size");

        Directory.CreateDirectory(targetDir);
        var path = Path.Combine(targetDir, ExeName);
        entry.ExtractToFile(path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Thay tệp đang chạy: đổi tên exe hiện tại thành ".old" (Windows cho đổi tên tệp đang chạy), chép bản mới vào chỗ cũ,
    /// rồi khóa (chỉ cho đọc) và kiểm tra lại SHA-256. Người gọi giữ <see cref="InstalledUpdate"/> tới khi đã khởi động
    /// tiến trình mới (không tiến trình nào tráo được tệp giữa lúc kiểm tra và lúc chạy), và gọi
    /// <see cref="InstalledUpdate.Rollback"/> nếu không khởi động được. Lỗi trong lúc thay -> tự khôi phục bản cũ.
    /// </summary>
    public static InstalledUpdate Install(PreparedUpdate update, string currentExe)
    {
        try
        {
            var old = OldPath(currentExe);
            File.Move(currentExe, old, overwrite: true);
            FileStream? locked = null;
            try
            {
                // Chép (không di chuyển) để tệp mới nhận quyền truy cập của thư mục đích, không mang ACL "chỉ admin".
                File.Copy(update.ExePath, currentExe, overwrite: false);
                locked = new FileStream(currentExe, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!string.Equals(Sha256Of(locked), update.ExeSha256, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateIntegrityException("installed file changed");
                return new InstalledUpdate(currentExe, old, locked);
            }
            catch (Exception ex)
            {
                locked?.Dispose();
                Log.Warn("Thay tệp cập nhật lỗi, khôi phục bản cũ", ex);
                Restore(currentExe, old);
                throw;
            }
        }
        finally
        {
            SecureTemp.Delete(update.Directory);
        }
    }

    /// <summary>Đưa exe cũ (".old") về chỗ cũ, ghi đè bản mới; thử lại vài lần nếu tệp đang bị phần mềm diệt virus mở.</summary>
    internal static void Restore(string currentExe, string old)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(old, currentExe, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 10)
            {
                Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                Log.Error($"Không khôi phục được {currentExe} từ {old}", ex);
                throw;
            }
        }
    }

    /// <summary>Gọi khi khởi động: xóa exe cũ còn sót sau lần cập nhật trước (đợi tiến trình cũ thoát hẳn).</summary>
    public static async Task CleanupAsync(string currentExe)
    {
        var old = OldPath(currentExe);
        for (var i = 0; i < 20 && File.Exists(old); i++)
        {
            if (TryDelete(old)) break;
            await Task.Delay(1000).ConfigureAwait(false);
        }
    }

    public static string OldPath(string exe) => exe + ".old";

    /// <summary>Ghi chú phát hành (Markdown) -> văn bản thường dễ đọc.</summary>
    public static string ToPlainText(string markdown)
    {
        var sb = new StringBuilder();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("<!--", StringComparison.Ordinal)) continue;
            line = HeadingRegex().Replace(line, "");
            line = BulletRegex().Replace(line, "$1• ");
            line = LinkRegex().Replace(line, "$1");
            line = line.Replace("**", "").Replace("__", "").Replace("`", "");
            sb.AppendLine(line);
        }

        return MultiBlankRegex().Replace(sb.ToString(), "\n\n").Trim();
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256Of(stream);
    }

    private static string Sha256Of(Stream stream)
    {
        stream.Position = 0;
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        return hash;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PCCheck-Updater/1.0");
        return http;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\s*)[\*\-\+]\s+")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiBlankRegex();
}

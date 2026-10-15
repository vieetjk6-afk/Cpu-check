using System.IO.Compression;
using PcCheck.Core.Updates;

namespace PcCheck.Core.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pccheck-update-tests-" + Guid.NewGuid().ToString("N"));

    public UpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const string ReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "name": "PC Check 1.2.0",
          "draft": false,
          "prerelease": false,
          "html_url": "https://github.com/vieetjk6-afk/Cpu-check/releases/tag/v1.2.0",
          "published_at": "2026-10-20T08:00:00Z",
          "body": "## What's Changed\n* Widget mới by @someone in [#12](https://example.com/12)\n\n\n**Full Changelog**: v1.1.0...v1.2.0",
          "assets": [
            { "name": "PcCheck-portable-win-x64.zip", "browser_download_url": "https://example.com/p.zip", "size": 68000000,
              "digest": "sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789" },
            { "name": "PcCheck-small-win-x64.zip", "browser_download_url": "https://example.com/s.zip", "size": 2600000 }
          ]
        }
        """;

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.1-beta.1", "2.0.1")]
    [InlineData("v1.0.0+build.5", "1.0.0")]
    [InlineData("v2", "2.0.0")]
    public void VersionsAreParsedFromTags(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(tag));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("vNext")]
    public void NonVersionTagsAreIgnored(string tag) => Assert.Null(UpdateService.ParseVersion(tag));

    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.2", "1.2.0", false)]
    [InlineData("1.1.0", "1.2.0", false)]
    [InlineData("2.0.0", "1.99.99", true)]
    public void NewerVersionIsDetected(string candidate, string current, bool newer)
    {
        Assert.Equal(newer, UpdateService.IsNewer(Version.Parse(candidate), Version.Parse(current)));
    }

    [Fact]
    public void ReleaseJsonIsParsed()
    {
        var release = UpdateService.ParseRelease(ReleaseJson)!;

        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("v1.2.0", release.Tag);
        Assert.Equal("PC Check 1.2.0", release.Name);
        Assert.Equal(2, release.Assets.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), release.Published);

        var portable = UpdateService.PickAsset(release, "portable")!;
        Assert.Equal("https://example.com/p.zip", portable.DownloadUrl);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", portable.Sha256);
        Assert.Null(UpdateService.PickAsset(release, "small")!.Sha256);
        Assert.Null(UpdateService.PickAsset(release, "arm64"));
    }

    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    [InlineData("\"tag_name\": \"v1.2.0\"", "\"tag_name\": \"nightly\"")]
    public void DraftsPrereleasesAndOddTagsAreSkipped(string from, string to)
    {
        Assert.Null(UpdateService.ParseRelease(ReleaseJson.Replace(from, to, StringComparison.Ordinal)));
    }

    [Fact]
    public void ReleaseNotesBecomePlainText()
    {
        var text = UpdateService.ToPlainText(UpdateService.ParseRelease(ReleaseJson)!.Notes);

        Assert.StartsWith("What's Changed", text, StringComparison.Ordinal);
        Assert.Contains("• Widget mới by @someone in #12", text, StringComparison.Ordinal);
        Assert.Contains("Full Changelog: v1.1.0...v1.2.0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", text, StringComparison.Ordinal);
    }

    private string MakeZip(string entryName, int size)
    {
        var zip = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        archive.CreateEntry("readme.txt").Open().Dispose();
        using var stream = archive.CreateEntry(entryName).Open();
        stream.Write(new byte[size]);
        return zip;
    }

    [Fact]
    public void OnlyTheExecutableIsExtracted()
    {
        var zip = MakeZip("publish/PcCheck.exe", 200_000);
        var target = Path.Combine(_dir, "out");

        var exe = UpdateService.ExtractExecutable(zip, target);

        Assert.Equal(Path.Combine(target, "PcCheck.exe"), exe);
        Assert.Equal(200_000, new FileInfo(exe).Length);
        Assert.Single(Directory.GetFiles(target));
    }

    [Fact]
    public void ArchiveWithoutExecutableIsRejected()
    {
        var zip = MakeZip("Other.exe", 200_000);
        Assert.Throws<UpdateIntegrityException>(() => UpdateService.ExtractExecutable(zip, Path.Combine(_dir, "out")));
    }

    [Fact]
    public void InstallReplacesExecutableAndKeepsOldCopy()
    {
        var current = Path.Combine(_dir, "PcCheck.exe");
        File.WriteAllText(current, "old");
        var prepared = Prepare("new");

        using (UpdateService.Install(prepared, current))
        {
            // Đang bị khóa: không ghi được (Linux không áp dụng chia sẻ tệp kiểu Windows; CI chạy kiểm thử trên Windows).
            if (OperatingSystem.IsWindows()) Assert.ThrowsAny<IOException>(() => File.WriteAllText(current, "tampered"));
        }

        Assert.Equal("new", File.ReadAllText(current));
        Assert.Equal("old", File.ReadAllText(UpdateService.OldPath(current)));
        Assert.False(Directory.Exists(prepared.Directory));
    }

    [Fact]
    public void InstallRollsBackWhenHashDoesNotMatch()
    {
        var current = Path.Combine(_dir, "PcCheck.exe");
        File.WriteAllText(current, "old");
        var prepared = Prepare("new") with { ExeSha256 = new string('0', 64) };

        Assert.Throws<UpdateIntegrityException>(() => UpdateService.Install(prepared, current));

        Assert.Equal("old", File.ReadAllText(current));
        Assert.False(File.Exists(UpdateService.OldPath(current)));
    }

    [Fact]
    public void RollbackRestoresOldExecutable()
    {
        var current = Path.Combine(_dir, "PcCheck.exe");
        File.WriteAllText(current, "old");
        var installed = UpdateService.Install(Prepare("new"), current);

        installed.Rollback();

        Assert.Equal("old", File.ReadAllText(current));
        Assert.False(File.Exists(UpdateService.OldPath(current)));
    }

    [Fact]
    public void InstallReplacesLeftoverOldCopy()
    {
        var current = Path.Combine(_dir, "PcCheck.exe");
        File.WriteAllText(current, "old");
        File.WriteAllText(UpdateService.OldPath(current), "older");

        using (UpdateService.Install(Prepare("new"), current))
        {
        }

        Assert.Equal("new", File.ReadAllText(current));
        Assert.Equal("old", File.ReadAllText(UpdateService.OldPath(current)));
    }

    [Fact]
    public void FailedInstallStillRemovesDownloadFolder()
    {
        var prepared = Prepare("new");
        Assert.ThrowsAny<IOException>(() => UpdateService.Install(prepared, Path.Combine(_dir, "missing", "PcCheck.exe")));
        Assert.False(Directory.Exists(prepared.Directory));
    }

    [Fact]
    public void ErrorsAreMappedToShortCodes()
    {
        Assert.Equal("rate_limit", UpdateService.ErrorCode(new HttpRequestException("x", null, System.Net.HttpStatusCode.Forbidden)));
        Assert.Equal("rate_limit", UpdateService.ErrorCode(new HttpRequestException("x", null, System.Net.HttpStatusCode.TooManyRequests)));
        Assert.Equal("HTTP 502", UpdateService.ErrorCode(new HttpRequestException("x", null, System.Net.HttpStatusCode.BadGateway)));
        Assert.Equal("network", UpdateService.ErrorCode(new HttpRequestException("dns")));
        Assert.Equal("timeout", UpdateService.ErrorCode(new TaskCanceledException()));
        Assert.Equal("invalid", UpdateService.ErrorCode(new System.Text.Json.JsonException()));
    }

    [Fact]
    public async Task CleanupRemovesOldExecutable()
    {
        var current = Path.Combine(_dir, "PcCheck.exe");
        File.WriteAllText(current, "new");
        File.WriteAllText(UpdateService.OldPath(current), "old");

        await UpdateService.CleanupAsync(current);

        Assert.False(File.Exists(UpdateService.OldPath(current)));
    }

    private PreparedUpdate Prepare(string content)
    {
        var dir = Path.Combine(_dir, "prepared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "PcCheck.exe");
        File.WriteAllText(exe, content);
        return new PreparedUpdate(dir, exe, UpdateService.Sha256Of(exe));
    }
}

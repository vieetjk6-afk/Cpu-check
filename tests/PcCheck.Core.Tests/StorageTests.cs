using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Storage;

namespace PcCheck.Core.Tests;

public sealed class StorageTests
{
    private static readonly string[] Roots =
    [
        @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData", @"C:\Users", @"C:\Users\An",
        @"C:\Users\An\AppData", @"C:\Users\An\AppData\Local", @"C:\Users\An\AppData\Roaming", @"C:\Users\An\Desktop",
        @"C:\Users\An\Downloads", @"C:\Users\An\Documents",
    ];

    private static DeleteBlock Check(string path, params string[] protectedPatterns) =>
        PathGuard.Check(path, @"C:\Windows", Roots, protectedPatterns);

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\drivers")]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"D:\System Volume Information")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21")]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.Photos_1")]
    public void SystemLocationsAreNeverDeleted(string path)
    {
        Assert.Equal(DeleteBlock.System, Check(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\An")]
    [InlineData(@"C:\Users\An\AppData\Local")]
    [InlineData(@"C:\Users\An\Downloads\")]
    [InlineData(@"c:\users\an\desktop")]
    public void ImportantRootsAreBlockedButNotTheirContents(string path)
    {
        Assert.Equal(DeleteBlock.SystemFolder, Check(path));
    }

    [Theory]
    [InlineData(@"C:\Users\An\Downloads\setup.exe")]
    [InlineData(@"C:\Users\An\AppData\Local\Temp\big.tmp")]
    [InlineData(@"D:\Games\Old Game")]
    [InlineData(@"C:\Program Files\OldApp")]
    public void OrdinaryItemsCanBeDeleted(string path)
    {
        Assert.Equal(DeleteBlock.None, Check(path));
    }

    [Fact]
    public void ProtectedPathsBlockTheFolderItsContentsAndItsParents()
    {
        string[] patterns = [@"D:\Work\Important"];
        Assert.Equal(DeleteBlock.Protected, Check(@"D:\Work\Important", patterns));
        Assert.Equal(DeleteBlock.Protected, Check(@"D:\Work\Important\report.docx", patterns));

        // Xóa thư mục cha cũng xóa luôn mục được bảo vệ.
        Assert.Equal(DeleteBlock.Protected, Check(@"D:\Work", patterns));
        Assert.Equal(DeleteBlock.None, Check(@"D:\Work\Other", patterns));
        Assert.Equal(DeleteBlock.None, Check(@"D:\Work\ImportantNot", patterns));
    }

    [Fact]
    public void ProtectedPatternsSupportWildcards()
    {
        string[] patterns = [@"*\node_modules\.cache", @"D:\Games\*"];
        Assert.True(ProtectedPaths.IsProtected(@"C:\src\app\node_modules\.cache", patterns));
        Assert.True(ProtectedPaths.IsProtected(@"C:\src\app\node_modules\.cache\x\y.bin", patterns));
        Assert.True(ProtectedPaths.IsProtected(@"d:\games\steam\steamapps", patterns));
        Assert.False(ProtectedPaths.IsProtected(@"C:\src\app\node_modules\react", patterns));
        Assert.False(ProtectedPaths.IsProtected(@"D:\Gamesave", patterns));
        Assert.False(ProtectedPaths.IsProtected(@"C:\anything", []));
    }

    [Fact]
    public void ProtectedPathsNormalizeInput()
    {
        try
        {
            ProtectedPaths.Set([@"  ""D:/Keep/""  ", "", "   ", @"D:\Keep"]);
            Assert.Single(ProtectedPaths.Patterns);
            Assert.True(ProtectedPaths.IsProtected(@"D:\Keep\a.txt"));
        }
        finally
        {
            ProtectedPaths.Set([]);
        }
    }

    private static readonly DiskAnalyzer.KnownFolders Known = new(@"C:\Windows", @"C:\Users\An\Downloads", @"C:\Users\An\AppData\Local\Temp\", @"C:\Users\An\AppData\Local");

    [Theory]
    [InlineData(@"D:\code\web\node_modules", true, "node_modules")]
    [InlineData(@"C:\Users\An\AppData\Local\npm-cache", true, "dev_cache")]
    [InlineData(@"C:\Users\An\AppData\Local\pip", true, "dev_cache")]
    [InlineData(@"D:\music\pip", true, null)]
    [InlineData(@"C:\Windows\Installer", true, "msi_installer")]
    [InlineData(@"D:\Setup\Installer", true, null)]
    [InlineData(@"C:\Windows.old", true, "windows_old")]
    [InlineData(@"C:\Users\An\Downloads", true, "downloads")]
    [InlineData(@"C:\Users\An\AppData\Local\Temp", true, "temp")]
    [InlineData(@"C:\Windows\Temp", true, "temp")]
    [InlineData(@"C:\hiberfil.sys", false, "hiberfil")]
    [InlineData(@"C:\Users\An\Downloads\vlc-3.0.20-win64.exe", false, "installer")]
    [InlineData(@"C:\Users\An\Videos\movie.mp4", false, null)]
    [InlineData(@"D:\Photos", true, null)]
    public void InsightsRecognizeKnownHeavyLocations(string path, bool isDirectory, string? expected)
    {
        Assert.Equal(expected, DiskAnalyzer.Insight(path, isDirectory, Known));
    }

    [Fact]
    public void EveryInsightAndDynamicStorageKeyIsTranslated()
    {
        foreach (var lang in new[] { Loc.Vietnamese, Loc.English })
        {
            var dict = Loc.Load(lang);
            foreach (var key in DiskAnalyzer.AllInsightKeys) Assert.True(dict.ContainsKey("storage.insight." + key), $"{lang}: storage.insight.{key}");
            foreach (var kind in new[] { "installer", "image", "archive" }) Assert.True(dict.ContainsKey("installer.kind." + kind), kind);
            foreach (var source in new[] { OperationLog.SourceJunk, OperationLog.SourceLeftovers, OperationLog.SourceDisk, OperationLog.SourceInstallers, OperationLog.SourcePurge })
            {
                Assert.True(dict.ContainsKey("history.source." + source), source);
            }

            foreach (var action in new[] { OperationLog.ActionRemoved, OperationLog.ActionRecycled, OperationLog.ActionSkipped, OperationLog.ActionFailed })
            {
                Assert.True(dict.ContainsKey("history.action." + action), action);
            }
        }
    }

    // ===== Tệp cài đặt =====

    [Theory]
    [InlineData("ChromeSetup.exe", "Google Chrome")]
    [InlineData("vlc-3.0.20-win64.exe", "VLC media player")]
    [InlineData("ZaloSetup-24.10.1.exe", "Zalo")]
    [InlineData("python-3.12.0-amd64.exe", "Python 3.12.0 (64-bit)")]
    [InlineData("DiscordSetup.exe", "Discord")]
    [InlineData("UltraViewer_setup_6.6_vi.exe", "UltraViewer version 6.6.58")]
    public void InstallersMatchTheirInstalledProgram(string file, string program)
    {
        Assert.Equal(program, InstallerFinder.MatchInstalled(file, ["Microsoft Edge", program, "Steam"]));
    }

    [Theory]
    [InlineData("setup.exe", "Steam")]
    [InlineData("game.iso", "Microsoft Office")]
    [InlineData("photos.zip", "Adobe Photoshop 2024")]
    [InlineData("player.exe", "VLC media player")]
    [InlineData("MicrosoftEdgeSetup.exe", "Microsoft Office")]
    public void UnrelatedInstallersDoNotMatch(string file, string program)
    {
        Assert.Null(InstallerFinder.MatchInstalled(file, [program]));
    }

    private static InstallerFile File(string name, string kind, int ageDays, string? installed = null) => new()
    {
        Name = name,
        Path = @"C:\Users\An\Downloads\" + name,
        Bytes = 50_000_000,
        LastWriteUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-ageDays),
        Kind = kind,
        InstalledMatch = installed,
    };

    [Fact]
    public void OnlyOldAndClearlyDisposableInstallersAreSuggested()
    {
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(InstallerFinder.IsSuggested(File("ChromeSetup.exe", "installer", 30), now));
        Assert.True(InstallerFinder.IsSuggested(File("tool.exe", "installer", 30, installed: "Tool"), now));
        Assert.True(InstallerFinder.IsSuggested(File("office.msi", "installer", 30), now));
        Assert.True(InstallerFinder.IsSuggested(File("win11.iso", "image", 30), now));

        // Mới tải về, tệp nén, hay .exe không rõ là bộ cài (có thể là phần mềm portable) -> không chọn sẵn.
        Assert.False(InstallerFinder.IsSuggested(File("ChromeSetup.exe", "installer", 2), now));
        Assert.False(InstallerFinder.IsSuggested(File("photos.zip", "archive", 300), now));
        Assert.False(InstallerFinder.IsSuggested(File("portable-tool.exe", "installer", 300), now));
    }

    [Theory]
    [InlineData(".EXE", "installer")]
    [InlineData(".msixbundle", "installer")]
    [InlineData(".iso", "image")]
    [InlineData(".7z", "archive")]
    [InlineData(".docx", null)]
    public void InstallerKinds(string extension, string? expected)
    {
        Assert.Equal(expected, InstallerFinder.KindOf(extension));
    }

    // ===== Thư mục build dự án, xóa tệp, lịch sử (dùng thư mục tạm thật) =====

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "pccheck-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Dir(params string[] parts)
        {
            var path = Path.Combine([Root, .. parts]);
            Directory.CreateDirectory(path);
            return path;
        }

        public string FileOf(long bytes, params string[] parts)
        {
            var path = Path.Combine([Root, .. parts]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task PurgeFindsOnlyArtifactsOfRealProjects()
    {
        using var temp = new TempDir();
        temp.FileOf(10, "web", "package.json");
        temp.FileOf(5000, "web", "node_modules", "react", "index.js");
        temp.FileOf(3000, "web", "dist", "app.js");
        temp.FileOf(10, "api", "Api.csproj");
        temp.FileOf(2000, "api", "bin", "Debug", "Api.dll");
        temp.FileOf(1000, "api", "obj", "project.assets.json");
        temp.FileOf(10, "py", "pyvenv-holder.txt");
        temp.FileOf(10, "py", ".venv", "pyvenv.cfg");
        temp.FileOf(4000, "py", ".venv", "lib", "site.py");

        // Không phải dự án: thư mục "build"/"bin"/"dist" bình thường phải được giữ nguyên.
        temp.FileOf(7000, "photos", "build", "holiday.jpg");
        temp.FileOf(7000, "tools", "bin", "tool.exe");
        temp.FileOf(7000, "notes", "dist", "x.txt");

        // Thư mục node_modules lồng trong node_modules không được liệt kê riêng.
        temp.FileOf(10, "web", "node_modules", "react", "package.json");
        temp.FileOf(100, "web", "node_modules", "react", "node_modules", "x", "i.js");

        var found = await ProjectPurge.ScanAsync([temp.Root]);
        var names = found.Select(f => Path.GetRelativePath(temp.Root, f.Path).Replace('\\', '/')).OrderBy(n => n).ToList();
        Assert.Equal(["api/bin", "api/obj", "py/.venv", "web/dist", "web/node_modules"], names);

        var nodeModules = found.Single(f => f.Kind == "node_modules");
        Assert.True(nodeModules.IsDependency);
        Assert.Equal(5110, nodeModules.Bytes);
        Assert.Equal("web", nodeModules.ProjectName);
        Assert.True(nodeModules.IsRecent); // vừa tạo xong
        Assert.False(found.Single(f => f.Kind == "dist").IsDependency);
    }

    [Fact]
    public async Task PurgeSkipsProtectedFolders()
    {
        using var temp = new TempDir();
        temp.FileOf(10, "web", "package.json");
        temp.FileOf(5000, "web", "node_modules", "a.js");
        try
        {
            ProtectedPaths.Set([Path.Combine(temp.Root, "web")]);
            Assert.Empty(await ProjectPurge.ScanAsync([temp.Root]));
        }
        finally
        {
            ProtectedPaths.Set([]);
        }
    }

    [Fact]
    public async Task PurgeDeleteRechecksTheProjectBeforeDeleting()
    {
        using var temp = new TempDir();
        temp.FileOf(10, "web", "package.json");
        temp.FileOf(5000, "web", "node_modules", "a.js");
        var artifact = (await ProjectPurge.ScanAsync([temp.Root])).Single();

        // package.json đã bị xóa sau khi quét -> không còn chắc là thư mục build -> giữ nguyên.
        System.IO.File.Delete(Path.Combine(temp.Root, "web", "package.json"));
        var refused = await ProjectPurge.DeleteAsync(artifact);
        Assert.Equal(1, refused.FailedFiles);
        Assert.True(Directory.Exists(artifact.Path));

        temp.FileOf(10, "web", "package.json");
        var result = await ProjectPurge.DeleteAsync(artifact);
        Assert.True(result.Complete);
        Assert.Equal(5000, result.FreedBytes);
        Assert.False(Directory.Exists(artifact.Path));
        Assert.True(System.IO.File.Exists(Path.Combine(temp.Root, "web", "package.json")));
    }

    [Fact]
    public void DeleteTreeNeverFollowsLinks()
    {
        using var temp = new TempDir();
        var outside = temp.FileOf(100, "outside", "keep.txt");
        temp.FileOf(200, "victim", "a.txt");
        var link = Path.Combine(temp.Root, "victim", "link");
        try
        {
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(outside)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // Windows không có quyền tạo symlink: bỏ qua phần này.
        }

        var result = FileDeleter.DeleteTree(Path.Combine(temp.Root, "victim"));
        Assert.False(Directory.Exists(Path.Combine(temp.Root, "victim")));
        Assert.True(System.IO.File.Exists(outside));
        Assert.Equal(200, result.FreedBytes);
    }

    [Fact]
    public async Task AnalyzerMeasuresFoldersAndFindsLargeFiles()
    {
        using var temp = new TempDir();
        temp.FileOf(3000, "small", "a.bin");
        temp.FileOf(2000, "small", "deep", "b.bin");
        var big = temp.FileOf(DiskAnalyzer.LargeFileMinBytes + 1, "big", "video.mkv");
        temp.FileOf(500, "root.txt");

        var progress = new DiskScanProgress();
        var report = await DiskAnalyzer.ScanAsync(temp.Root, progress);

        Assert.Equal(ScanStatus.Complete, report.Status);
        Assert.Equal(["big", "small", "root.txt"], report.Entries.Select(e => e.Name).ToList());
        Assert.Equal(5000, report.Entries.Single(e => e.Name == "small").Bytes);
        Assert.Equal(2, report.Entries.Single(e => e.Name == "small").Files);
        Assert.Equal(4, report.TotalFiles);
        Assert.Equal(DiskAnalyzer.LargeFileMinBytes + 5501, report.TotalBytes);
        Assert.Equal(big, Assert.Single(report.LargeFiles).Path);
        Assert.Equal(report.TotalFiles, progress.Files);

        var (bytes, files, status) = DiskAnalyzer.Measure(Path.Combine(temp.Root, "small"));
        Assert.Equal((5000L, 2L, ScanStatus.Complete), (bytes, files, status));
    }

    [Fact]
    public void OperationLogKeepsNewestFirstAndSurvivesBadLines()
    {
        using var temp = new TempDir();
        var previous = OperationLog.FilePath;
        OperationLog.FilePath = Path.Combine(temp.Root, "history.jsonl");
        try
        {
            OperationLog.Record(OperationLog.SourceJunk, OperationLog.ActionRemoved, "Temp", 1000, 10);
            System.IO.File.AppendAllText(OperationLog.FilePath, "{not json\n");
            OperationLog.Record(OperationLog.SourceDisk, OperationLog.ActionRecycled, @"D:\old.iso", 5000);

            var records = OperationLog.Read();
            Assert.Equal(2, records.Count);
            Assert.Equal(@"D:\old.iso", records[0].Target);
            Assert.Equal(OperationLog.ActionRecycled, records[0].Action);
            Assert.Equal(10, records[1].Count);

            OperationLog.Clear();
            Assert.Empty(OperationLog.Read());
        }
        finally
        {
            OperationLog.FilePath = previous;
        }
    }
}

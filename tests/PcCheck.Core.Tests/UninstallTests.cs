using PcCheck.Core.Cleanup;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;

namespace PcCheck.Core.Tests;

public sealed class UninstallTests
{
    private static InstalledProgram Program(string name, string publisher = "", string uninstall = "", string quiet = "") => new()
    {
        Name = name,
        Publisher = publisher,
        UninstallCommand = uninstall,
        QuietUninstallCommand = quiet,
    };

    [Theory]
    [InlineData("WinRAR 6.24 (64-bit)", "WinRAR")]
    [InlineData("Mozilla Firefox (x64 vi)", "Mozilla Firefox")]
    [InlineData("Zalo 24.10.1", "Zalo")]
    [InlineData("Notepad++ (64-bit x64)", "Notepad++")]
    [InlineData("Zoom Workplace", "Zoom Workplace")]
    [InlineData("7-Zip 23.01 (x64)", "7-Zip")]
    [InlineData("Microsoft SQL Server 2019 (64-bit)", "Microsoft SQL Server 2019")]
    public void CleanNameDropsVersionAndArchitecture(string name, string expected)
    {
        Assert.Equal(expected, LeftoverRules.CleanName(name));
    }

    [Fact]
    public void FolderMatchingIsStrict()
    {
        Assert.True(LeftoverRules.FolderMatches("WinRAR", Program("WinRAR 6.24 (64-bit)")));
        Assert.True(LeftoverRules.FolderMatches("UniKey", Program("UniKey 4.6")));
        Assert.True(LeftoverRules.FolderMatches("7-Zip", Program("7-Zip 23.01 (x64)")));

        // Tên hãng dùng chung, tên quá ngắn hoặc chỉ trùng một phần không bao giờ được coi là của phần mềm.
        Assert.False(LeftoverRules.FolderMatches("Microsoft", Program("Microsoft")));
        Assert.False(LeftoverRules.FolderMatches("Common Files", Program("Common Files")));
        Assert.False(LeftoverRules.FolderMatches("Zoom", Program("Zoom Workplace")));
        Assert.False(LeftoverRules.FolderMatches("VLC", Program("VLC")));

        // Thư mục dùng chung của mọi phiên bản (chứa dữ liệu .mdf) không khớp với một phiên bản cụ thể.
        Assert.False(LeftoverRules.FolderMatches("Microsoft SQL Server", Program("Microsoft SQL Server 2019 (64-bit)")));
        Assert.False(LeftoverRules.FolderMatches("PostgreSQL", Program("PostgreSQL")));
    }

    [Theory]
    [InlineData("MISA.mdf", true)]
    [InlineData("db_log.LDF", true)]
    [InlineData("backup.bak", true)]
    [InlineData("data.sqlite", true)]
    [InlineData("PG_VERSION", true)]
    [InlineData("ibdata1", true)]
    [InlineData("Báo cáo.xlsx", true)]
    [InlineData("save01.sav", true)]
    [InlineData("Thumbs.db", false)]
    [InlineData("app.exe", false)]
    [InlineData("settings.ini", false)]
    [InlineData("readme.pdf", false)]
    public void DataFilesAreRecognised(string fileName, bool isData)
    {
        Assert.Equal(isData, LeftoverRules.IsDataFile(fileName));
    }

    [Fact]
    public void DistinctiveNamesExcludeUmbrellaVendors()
    {
        Assert.True(LeftoverRules.IsDistinctive("WinRAR"));
        Assert.False(LeftoverRules.IsDistinctive("Microsoft"));
        Assert.False(LeftoverRules.IsDistinctive("Google"));
        Assert.False(LeftoverRules.IsDistinctive("abc"));
    }

    [Fact]
    public void PublisherMatchingIgnoresCompanySuffix()
    {
        var program = Program("Foxit PDF Reader", "Foxit Software Inc.");
        Assert.Equal("Foxit Software", LeftoverRules.CleanPublisher(program.Publisher));
        Assert.True(LeftoverRules.PublisherMatches("Foxit Software", program));
        Assert.False(LeftoverRules.PublisherMatches("Foxit Software", Program("Foxit PDF Reader")));
    }

    [Fact]
    public void ProtectedFoldersAreNeverRemoved()
    {
        string[] roots = [@"C:\Program Files", @"C:\Users\An\AppData\Roaming"];
        string[] others = [@"C:\Program Files\Foo\Bar"];
        bool Protected(string path) => LeftoverRules.IsProtectedFolder(path, roots, @"C:\Windows", others);

        Assert.False(Protected(@"C:\Program Files\WinRAR"));
        Assert.False(Protected(@"C:\Users\An\AppData\Roaming\WinRAR\"));

        Assert.True(Protected(@"C:\"));
        Assert.True(Protected(""));
        Assert.True(Protected(@"C:\Program Files"));
        Assert.True(Protected(@"c:\program files\"));
        Assert.True(Protected(@"C:\Users"));                       // chứa thư mục gốc đã biết
        Assert.True(Protected(@"C:\Windows\System32\drivers"));
        Assert.True(Protected(@"C:\Program Files\Common Files"));  // tên dùng chung
        Assert.True(Protected(@"C:\Program Files\Foo"));            // chứa phần mềm khác đang cài
        Assert.True(Protected(@"C:\Program Files\Foo\Bar\Data"));   // nằm trong phần mềm khác
        Assert.True(Protected(@"C:\Program Files\Microsoft SQL Server"));
        Assert.True(Protected(@"C:\Program Files (x86)\Microsoft SQL Server"));
        Assert.True(Protected(@"D:\PostgreSQL"));
    }

    [Fact]
    public void LastSegmentWorksOnWindowsPaths()
    {
        Assert.Equal("16", LeftoverRules.LastSegment(@"C:\Program Files\PostgreSQL\16\"));
        Assert.Equal("WinRAR", LeftoverRules.LastSegment(@"C:\Program Files\WinRAR"));
    }

    [Fact]
    public void ServicesAndRegistryAreRemovedBeforeFiles()
    {
        var order = Enum.GetValues<LeftoverKind>().OrderBy(LeftoverRemover.Order).ToList();
        Assert.Equal([LeftoverKind.Service, LeftoverKind.ScheduledTask, LeftoverKind.RegistryValue, LeftoverKind.RegistryKey,
            LeftoverKind.Shortcut, LeftoverKind.File, LeftoverKind.Folder], order);
    }

    [Fact]
    public void UnknownInstallStateCountsAsInstalled()
    {
        // Không đọc được mục gỡ cài đặt -> coi như còn cài, để không quét/xóa phần còn sót của phần mềm chưa gỡ xong.
        Assert.True(Uninstaller.IsInstalled(new InstalledProgram { Id = "", Name = "X" }));
        Assert.True(Uninstaller.IsInstalled(new InstalledProgram { Id = "garbage|id", Name = "X" }));
    }

    [Fact]
    public void PathHelpersCompareCaseInsensitively()
    {
        Assert.True(LeftoverRules.IsUnder(@"C:\Program Files\App\bin", @"c:\program files\app"));
        Assert.False(LeftoverRules.IsUnder(@"C:\Program Files\AppX", @"C:\Program Files\App"));
        Assert.False(LeftoverRules.IsUnder(@"C:\Program Files\App", @"C:\Program Files\App"));
        Assert.True(LeftoverRules.SamePath(@"C:/Tools/App/", @"c:\tools\app"));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\WinRAR\\uninstall.exe\" /S", @"C:\Program Files\WinRAR")]
    [InlineData(@"C:\Apps\Tool\tool.exe,0", @"C:\Apps\Tool")]
    [InlineData(@"C:\Apps\Tool\unins000.exe /SILENT", @"C:\Apps\Tool")]
    [InlineData("MsiExec.exe /X{12345678-1234-1234-1234-123456789012}", null)]
    [InlineData(@"C:\setup.exe", null)]
    [InlineData("", null)]
    public void FolderOfCommand(string command, string? expected)
    {
        Assert.Equal(expected, LeftoverRules.FolderOf(command));
    }

    [Theory]
    [InlineData(@"Software\WinRAR", true)]
    [InlineData(@"SOFTWARE\WOW6432Node\WinRAR", true)]
    [InlineData(@"Software\Foxit Software\Foxit PDF Reader", true)]
    [InlineData(@"Software", false)]
    [InlineData(@"Software\Microsoft", false)]
    [InlineData(@"Software\Microsoft\Windows", false)]
    [InlineData(@"Software\Microsoft\WinRAR", false)]
    [InlineData(@"Software\Classes\WinRAR", false)]
    [InlineData(@"Software\Policies\WinRAR", false)]
    [InlineData(@"Software\Foxit\Reader\Settings", false)]
    [InlineData(@"Software\abc", false)]
    [InlineData(@"System\CurrentControlSet\Services\WinRAR", false)]
    public void OnlyProductRegistryKeysAreSafe(string subKey, bool safe)
    {
        Assert.Equal(safe, LeftoverRules.IsSafeRegistryKey(subKey));
    }

    [Fact]
    public void MsiProgramsUninstallPassively()
    {
        var program = Program("Tool", uninstall: "MsiExec.exe /I{12345678-1234-1234-1234-123456789012}");

        var quiet = Uninstaller.PlanCommand(program, preferQuiet: true);
        Assert.Equal("msiexec.exe", quiet.File);
        Assert.Equal("/x {12345678-1234-1234-1234-123456789012} /passive /norestart", quiet.Args);
        Assert.True(quiet.Quiet);

        var normal = Uninstaller.PlanCommand(program, preferQuiet: false);
        Assert.Equal("/x {12345678-1234-1234-1234-123456789012}", normal.Args);
        Assert.False(normal.Quiet);
    }

    [Fact]
    public void QuietUninstallStringIsPreferredWhenAvailable()
    {
        var program = Program("App", uninstall: "\"C:\\Program Files\\App\\unins000.exe\"", quiet: "\"C:\\Program Files\\App\\unins000.exe\" /SILENT");

        var quiet = Uninstaller.PlanCommand(program, preferQuiet: true);
        Assert.Equal(@"C:\Program Files\App\unins000.exe", quiet.File);
        Assert.Equal("/SILENT", quiet.Args);
        Assert.True(quiet.Quiet);

        var normal = Uninstaller.PlanCommand(program, preferQuiet: false);
        Assert.Equal("", normal.Args);
        Assert.False(normal.Quiet);

        // Không có lệnh im lặng: vẫn mở trình gỡ bình thường.
        var noQuiet = Uninstaller.PlanCommand(Program("App", uninstall: "\"C:\\Program Files\\App\\uninstall.exe\""), preferQuiet: true);
        Assert.Equal(@"C:\Program Files\App\uninstall.exe", noQuiet.File);
        Assert.False(noQuiet.Quiet);
    }

    [Theory]
    [InlineData("NoErrorsFound\r\n", DiskCheckStatus.Healthy)]
    [InlineData("WARNING: something\nSpotFixNeeded", DiskCheckStatus.ErrorsFound)]
    [InlineData("ScanNeeded", DiskCheckStatus.ErrorsFound)]
    [InlineData("FullRepairNeeded\n", DiskCheckStatus.ErrorsFound)]
    public void RepairVolumeOutputIsParsed(string output, DiskCheckStatus expected)
    {
        Assert.Equal(expected, DiskChecker.ParseRepairVolume(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Repair-Volume : Access denied")]
    public void UnknownRepairVolumeOutputFallsBack(string output)
    {
        Assert.Null(DiskChecker.ParseRepairVolume(output));
    }

    [Theory]
    [InlineData(0, "Windows has scanned the file system and found no problems.", DiskCheckStatus.Healthy)]
    [InlineData(3, "Windows has scanned the file system and found problems.", DiskCheckStatus.ErrorsFound)]
    [InlineData(2, "Access is denied.", DiskCheckStatus.Failed)]
    [InlineData(3, "Truy cập bị từ chối.", DiskCheckStatus.Failed)]
    [InlineData(-1, "", DiskCheckStatus.Failed)]
    [InlineData(3, "Truy ca\u0323\u0302p bi\u0323 tu\u031B\u0300 cho\u0302\u0301i.", DiskCheckStatus.Failed)] // dấu tách rời (bảng mã OEM 1258)
    public void ChkdskOutputIsParsed(int exitCode, string output, DiskCheckStatus expected)
    {
        Assert.Equal(expected, DiskChecker.ParseChkdsk(exitCode, output));
    }

    [Theory]
    [InlineData(null, "C")]
    [InlineData("d:", "D")]
    [InlineData("E:\\", "E")]
    [InlineData("1", "C")]
    public void DriveLetterIsNormalized(string? drive, string expected)
    {
        Assert.Equal(expected, DiskChecker.Normalize(drive));
    }

    [Fact]
    public void DefenderExitCodesAreInterpreted()
    {
        var clean = DefenderScanner.Interpret(0, [], TimeSpan.FromMinutes(3));
        Assert.True(clean.Completed);
        Assert.False(clean.Failed);
        Assert.False(clean.ThreatsReported);

        var threats = DefenderScanner.Interpret(2, [], TimeSpan.FromMinutes(3));
        Assert.True(threats.ThreatsReported);
        Assert.False(threats.Failed);

        // Defender bị phần mềm diệt virus khác thay thế: MpCmdRun lỗi -> không được báo "sạch".
        var failed = DefenderScanner.Interpret(unchecked((int)0x800106BA), [], TimeSpan.FromSeconds(2));
        Assert.True(failed.Failed);
        Assert.False(failed.Completed);

        var namedButError = DefenderScanner.Interpret(1, ["Trojan:Win32/Test"], TimeSpan.FromMinutes(1));
        Assert.True(namedButError.ThreatsReported);
    }

    [Fact]
    public void UninstallTextsExist()
    {
        var vi = Loc.Load(Loc.Vietnamese);
        foreach (var kind in Enum.GetValues<LeftoverKind>()) Assert.True(vi.ContainsKey("leftover.kind." + kind.ToString().ToLowerInvariant()), kind.ToString());
        foreach (var reason in new[] { "install_dir", "program_dir", "data_dir", "large_data", "contains_data" }) Assert.True(vi.ContainsKey("leftover.reason." + reason), reason);
        foreach (var status in new[] { "pending", "running", "waiting", "removed", "stillinstalled", "failed", "skipped" })
        {
            Assert.True(vi.ContainsKey("uninstall.status." + status), status);
        }

        foreach (var flavor in new[] { "portable", "small" }) Assert.True(vi.ContainsKey("update.flavor." + flavor), flavor);
    }
}

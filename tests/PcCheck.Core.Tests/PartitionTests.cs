using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Partitions;
using PcCheck.Core.Util;

namespace PcCheck.Core.Tests;

public sealed class PartitionTests
{
    private const long MiB = PartitionLayout.MiB;
    private const long GiB = PartitionLayout.GiB;

    private static PartitionInfo Part(int number, long offset, long size, PartitionKind kind = PartitionKind.Data, string fs = "NTFS",
        char? letter = null, bool windows = false, bool boot = false, long? free = null, bool pagefile = false, bool logical = false) => new()
    {
        DiskNumber = 0,
        PartitionNumber = number,
        Offset = offset,
        Size = size,
        Kind = kind,
        FileSystem = kind is PartitionKind.Reserved ? "" : fs,
        DriveLetter = letter,
        IsWindows = windows,
        IsBootLoader = boot,
        FreeBytes = free,
        HasPageFile = pagefile,
        IsLogical = logical,
        VolumePath = kind is PartitionKind.Reserved ? "" : $@"\\?\Volume{{{number}}}\",
    };

    private static PhysicalDiskInfo Disk(long size, PartitionStyle style, bool system, params PartitionInfo[] parts)
    {
        var disk = new PhysicalDiskInfo { Number = 0, Name = "Test", Size = size, Style = style, IsSystemDisk = system, Partitions = parts.ToList() };
        disk.FreeRegions = PartitionLayout.FreeRegions(disk);
        return disk;
    }

    /// <summary>Ổ Windows GPT điển hình: EFI 100 MB, MSR 16 MB, C: 200 GB, vùng trống 50 GB, D: 200 GB, Recovery 1 GB.</summary>
    private static PhysicalDiskInfo SystemDisk(out PartitionInfo efi, out PartitionInfo msr, out PartitionInfo c, out PartitionInfo d, out PartitionInfo recovery)
    {
        efi = Part(1, MiB, 100 * MiB, PartitionKind.Efi, "FAT32", boot: true);
        msr = Part(2, 101 * MiB, 16 * MiB, PartitionKind.Reserved);
        c = Part(3, 117 * MiB, 200 * GiB, letter: 'C', windows: true, free: 80 * GiB, pagefile: true);
        d = Part(4, c.End + 50 * GiB, 200 * GiB, letter: 'D', free: 150 * GiB);
        recovery = Part(5, d.End, GiB, PartitionKind.Recovery);
        return Disk(recovery.End + 10 * MiB, PartitionStyle.Gpt, true, efi, msr, c, d, recovery);
    }

    [Theory]
    [InlineData("{C12A7328-F81F-11D2-BA4B-00A0C93EC93B}", 0, "", PartitionKind.Efi)]
    [InlineData("{e3c9e316-0b5c-4db8-817d-f92df00215ae}", 0, "", PartitionKind.Reserved)]
    [InlineData("{de94bba4-06d1-4d40-a16a-bfd50179d6ac}", 0, "", PartitionKind.Recovery)]
    [InlineData("{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", 0, "Basic", PartitionKind.Data)]
    [InlineData("{0fc63daf-8483-4772-8e79-3d69d8477de4}", 0, "Unknown", PartitionKind.Other)]
    [InlineData("{af9b60a0-1431-4f62-bc68-3311714a69ad}", 0, "", PartitionKind.Dynamic)]
    [InlineData("", 0x07, "IFS", PartitionKind.Data)]
    [InlineData("", 0x0C, "FAT32 XINT13", PartitionKind.Data)]
    [InlineData("", 0x27, "", PartitionKind.Recovery)]
    [InlineData("", 0x0F, "Extended", PartitionKind.Extended)]
    [InlineData("", 0x83, "", PartitionKind.Other)]
    [InlineData("", 0x42, "", PartitionKind.Dynamic)]
    [InlineData("", 0, "Reserved", PartitionKind.Reserved)]
    public void ClassifiesPartitionTypes(string gpt, int mbr, string type, PartitionKind expected) =>
        Assert.Equal(expected, PartitionLayout.Classify(gpt, mbr, type));

    [Fact]
    public void FindsUnallocatedRegionsAndIgnoresAlignmentGaps()
    {
        var disk = SystemDisk(out _, out _, out var c, out var d, out _);
        var region = Assert.Single(disk.FreeRegions); // 10 MB cuối ổ < ngưỡng 16 MB -> bỏ qua
        Assert.Equal(c.End, region.Offset);
        Assert.Equal(50 * GiB, region.Size);
        Assert.Equal(d.Offset, region.End);
    }

    [Fact]
    public void EmptyDiskIsOneBigRegionMinusGptReservedAreas()
    {
        var disk = Disk(100 * GiB, PartitionStyle.Gpt, false);
        var region = Assert.Single(disk.FreeRegions);
        Assert.Equal(MiB, region.Offset);
        Assert.Equal(100 * GiB - 2 * MiB, region.Size);
        Assert.Empty(Disk(100 * GiB, PartitionStyle.Raw, false).FreeRegions);
    }

    [Fact]
    public void LogicalPartitionsInsideExtendedAreNotCountedTwice()
    {
        var extended = Part(2, 51 * GiB, 40 * GiB, PartitionKind.Extended, "");
        var logical = Part(5, 51 * GiB + MiB, 20 * GiB, logical: true);
        var disk = Disk(100 * GiB, PartitionStyle.Mbr, false, Part(1, MiB, 51 * GiB - MiB), extended, logical);
        var region = Assert.Single(disk.FreeRegions);
        Assert.Equal(extended.End, region.Offset);
    }

    [Fact]
    public void ProtectsWindowsBootAndPageFilePartitions()
    {
        var disk = SystemDisk(out var efi, out var msr, out var c, out var d, out var recovery);

        Assert.Equal("part.block.windows", PartitionLayout.Blocker(disk, c, PartitionOp.Delete));
        Assert.Equal("part.block.windows", PartitionLayout.Blocker(disk, c, PartitionOp.Format));
        Assert.Equal("part.block.windows", PartitionLayout.Blocker(disk, c, PartitionOp.ChangeLetter));
        Assert.Equal("part.block.boot", PartitionLayout.Blocker(disk, efi, PartitionOp.Delete));
        Assert.Equal("part.block.boot", PartitionLayout.Blocker(disk, efi, PartitionOp.Format));
        Assert.Equal("part.block.msr", PartitionLayout.Blocker(disk, msr, PartitionOp.Delete));

        // Co / mở rộng ổ C: vẫn được (Windows hỗ trợ trực tuyến).
        Assert.Null(PartitionLayout.Blocker(disk, c, PartitionOp.Shrink));
        Assert.Null(PartitionLayout.Blocker(disk, c, PartitionOp.Extend));

        // Ổ dữ liệu và phân vùng khôi phục xóa được (giao diện vẫn hỏi xác nhận).
        Assert.Null(PartitionLayout.Blocker(disk, d, PartitionOp.Delete));
        Assert.Null(PartitionLayout.Blocker(disk, d, PartitionOp.Format));
        Assert.Null(PartitionLayout.Blocker(disk, recovery, PartitionOp.Delete));
        Assert.Equal("part.block.kind", PartitionLayout.Blocker(disk, recovery, PartitionOp.Format));
    }

    [Fact]
    public void ExtendNeedsFreeSpaceDirectlyAfterAndNtfs()
    {
        var disk = SystemDisk(out _, out _, out var c, out var d, out _);
        Assert.Null(PartitionLayout.Blocker(disk, c, PartitionOp.Extend));
        Assert.Equal("part.block.no_space_after", PartitionLayout.Blocker(disk, d, PartitionOp.Extend));

        var fat = Part(1, MiB, 10 * GiB, fs: "FAT32", letter: 'E');
        var usb = Disk(64 * GiB, PartitionStyle.Mbr, false, fat);
        Assert.Equal("part.block.extend_fs", PartitionLayout.Blocker(usb, fat, PartitionOp.Extend));
        Assert.Equal("part.block.shrink_fs", PartitionLayout.Blocker(usb, fat, PartitionOp.Shrink));
        Assert.Null(PartitionLayout.Blocker(usb, fat, PartitionOp.ConvertToNtfs));
        Assert.Equal("part.block.not_fat", PartitionLayout.Blocker(disk, d, PartitionOp.ConvertToNtfs));
    }

    [Fact]
    public void OfflineAndDynamicDisksAreReadOnlyInTheApp()
    {
        var p = Part(1, MiB, 10 * GiB, letter: 'E');
        var offline = new PhysicalDiskInfo { Size = 20 * GiB, Style = PartitionStyle.Gpt, IsOffline = true, Partitions = [p] };
        Assert.Equal("part.block.offline", PartitionLayout.Blocker(offline, p, PartitionOp.Delete));

        var dyn = Part(1, MiB, 10 * GiB, PartitionKind.Dynamic);
        var disk = Disk(20 * GiB, PartitionStyle.Gpt, false, dyn);
        Assert.Equal("part.block.dynamic", PartitionLayout.Blocker(disk, dyn, PartitionOp.Delete));
    }

    [Fact]
    public void MergeTakesNextDataPartitionAndRefusesProtectedOnes()
    {
        var disk = SystemDisk(out _, out _, out var c, out var d, out _);

        // C: + D: (có vùng trống ở giữa) – gộp được, D: bị xóa.
        Assert.Null(PartitionLayout.MergeBlocker(disk, c, out var next));
        Assert.Same(d, next);

        // D: + Recovery – phân vùng sau không phải dữ liệu.
        Assert.Equal("part.block.next_kind", PartitionLayout.MergeBlocker(disk, d, out _));

        // Phân vùng sau chứa pagefile -> chặn.
        var e = Part(1, MiB, 50 * GiB, letter: 'E', free: 40 * GiB);
        var f = Part(2, e.End, 50 * GiB, letter: 'F', pagefile: true);
        var disk2 = Disk(f.End + MiB, PartitionStyle.Mbr, false, e, f);
        Assert.Equal("part.block.pagefile_next", PartitionLayout.MergeBlocker(disk2, e, out _));
    }

    [Fact]
    public void MergeCopyNeedsRoomForTheData()
    {
        var keep = Part(1, MiB, 100 * GiB, letter: 'C', free: 30 * GiB);
        var small = Part(2, keep.End, 50 * GiB, letter: 'D', free: 30 * GiB); // 20 GB dữ liệu
        var big = Part(3, small.End, 50 * GiB, letter: 'E', free: 10 * GiB); // 40 GB dữ liệu
        var noLetter = Part(4, big.End, 50 * GiB, free: 49 * GiB);
        Assert.True(PartitionLayout.CanCopyForMerge(keep, small));
        Assert.False(PartitionLayout.CanCopyForMerge(keep, big));
        Assert.False(PartitionLayout.CanCopyForMerge(keep, noLetter));
    }

    [Theory]
    [InlineData("Data", "NTFS", "Data")]
    [InlineData("  My: Disk?* ", "NTFS", "My Disk")]
    [InlineData("A very long label that exceeds the limit", "NTFS", "A very long label that exceeds t")]
    [InlineData("USB.DRIVE+2024", "FAT32", "USBDRIVE202")]
    [InlineData("Ổ Dữ Liệu", "exFAT", "Ổ Dữ Liệu")]
    [InlineData(null, "NTFS", "")]
    public void SanitizesLabels(string? input, string fs, string expected) =>
        Assert.Equal(expected, PartitionLayout.SanitizeLabel(input, fs));

    [Theory]
    [InlineData("100", 100L * 1024)]
    [InlineData("1.5", 1536L)]
    [InlineData("1,5", 1536L)]
    [InlineData(" 0.5 ", 512L)]
    public void ParsesGigabytesAlignedToMegabytes(string text, long expectedMiB) =>
        Assert.Equal(expectedMiB * MiB, PartitionLayout.ParseGigabytes(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    public void RejectsInvalidSizes(string text) => Assert.Null(PartitionLayout.ParseGigabytes(text));

    [Fact]
    public void FileSystemLimits()
    {
        Assert.Null(PartitionLayout.FileSystemBlocker("FAT32", 32 * GiB));
        Assert.Equal("part.error.fat32_size", PartitionLayout.FileSystemBlocker("FAT32", 64 * GiB));
        Assert.Null(PartitionLayout.FileSystemBlocker("exFAT", 2000 * GiB));
        Assert.Equal("part.error.fs", PartitionLayout.FileSystemBlocker("ext4", GiB));
    }

    [Fact]
    public void FreeLettersSkipUsedOnesAndAB()
    {
        var free = PartitionLayout.FreeLetters(['C', 'd', 'Z']);
        Assert.Equal('E', free[0]);
        Assert.DoesNotContain('D', free);
        Assert.DoesNotContain('Z', free);
        Assert.DoesNotContain('A', free);
    }

    [Fact]
    public void CommandsOnlyContainValidatedValues()
    {
        Assert.Equal("Remove-Partition -DiskNumber 1 -PartitionNumber 3 -Confirm:$false", PartitionCommands.Delete(1, 3));
        Assert.Equal("Resize-Partition -DiskNumber 0 -PartitionNumber 3 -Size 107374182400 -Confirm:$false", PartitionCommands.Resize(0, 3, 100 * GiB));

        var create = PartitionCommands.Create(1, MiB, 10 * GiB, 'e', "ntfs", "It's 'mine'", false);
        Assert.Contains("-DriveLetter E", create);
        Assert.Contains("-FileSystem NTFS", create);
        Assert.Contains("-NewFileSystemLabel 'It''s ''mine'''", create);
        Assert.DoesNotContain("-Full", create);
        Assert.Contains("-AssignDriveLetter", PartitionCommands.Create(1, MiB, GiB, null, "exFAT", "", true));
        Assert.Contains("-Full", PartitionCommands.Format(1, 2, "exFAT", "x", true));

        Assert.Throws<ArgumentException>(() => PartitionCommands.Letter('1'));
        Assert.Throws<ArgumentException>(() => PartitionCommands.FileSystem("ext4; Remove-Item"));
        Assert.Throws<ArgumentException>(() => PartitionCommands.Initialize(0, PartitionStyle.Raw));
        Assert.Equal("Initialize-Disk -Number 2 -PartitionStyle GPT", PartitionCommands.Initialize(2, PartitionStyle.Gpt));
        Assert.Equal(@"Remove-PartitionAccessPath -DiskNumber 0 -PartitionNumber 4 -AccessPath 'F:\'", PartitionCommands.RemoveLetter(0, 4, 'f'));
    }

    [Fact]
    public void MergeCopyArguments()
    {
        var folder = PartitionCommands.MergeFolderName('d', "Merged from");
        Assert.Equal("Merged from D", folder);
        Assert.Equal("Merged from D", PartitionCommands.MergeFolderName('D', "\"/\\"));
        var args = PartitionCommands.MergeCopyArgs('D', 'C', folder);
        Assert.StartsWith(@"D:\ ""C:\Merged from D"" /E", args);
        Assert.Contains("System Volume Information", args);
        Assert.True(PartitionCommands.RobocopySucceeded(1));
        Assert.True(PartitionCommands.RobocopySucceeded(0));
        Assert.False(PartitionCommands.RobocopySucceeded(8));
        Assert.False(PartitionCommands.RobocopySucceeded(-1));
    }

    [Fact]
    public void ParsesSupportedSizeOutput()
    {
        Assert.Equal(new PartitionSizeRange(1000, 5000), PartitionService.ParseSupportedSize("warning\r\n1000|5000\r\n"));
        Assert.Null(PartitionService.ParseSupportedSize("Access denied"));
        Assert.Null(PartitionService.ParseSupportedSize("5000|1000"));
    }

    [Fact]
    public void ConsoleSafeLabels()
    {
        Assert.True(PartitionService.IsConsoleSafeLabel(""));
        Assert.True(PartitionService.IsConsoleSafeLabel("USB DRIVE_1-2"));
        Assert.False(PartitionService.IsConsoleSafeLabel("A&B"));
        Assert.False(PartitionService.IsConsoleSafeLabel("Dữ liệu"));
    }

    [Fact]
    public void AssemblesWmiRowsIntoDisks()
    {
        var disks = new List<WmiRow>
        {
            new() { ["Number"] = 0u, ["FriendlyName"] = "NVMe SSD", ["Size"] = (ulong)(512 * GiB), ["PartitionStyle"] = (ushort)2, ["IsBoot"] = true, ["BusType"] = (ushort)17 },
            new() { ["Number"] = 1u, ["FriendlyName"] = "USB Flash", ["Size"] = (ulong)(32 * GiB), ["PartitionStyle"] = (ushort)0, ["BusType"] = (ushort)7 },
        };
        var partitions = new List<WmiRow>
        {
            new() { ["DiskNumber"] = 0u, ["PartitionNumber"] = 1u, ["Offset"] = (ulong)MiB, ["Size"] = (ulong)(100 * MiB), ["GptType"] = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}", ["Type"] = "System", ["IsSystem"] = true, ["DriveLetter"] = '\0', ["AccessPaths"] = new[] { @"\\?\Volume{a}\" } },
            new() { ["DiskNumber"] = 0u, ["PartitionNumber"] = 3u, ["Offset"] = (ulong)(117 * MiB), ["Size"] = (ulong)(400 * GiB), ["GptType"] = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", ["Type"] = "Basic", ["IsBoot"] = true, ["DriveLetter"] = 'C', ["AccessPaths"] = new[] { "C:\\", @"\\?\Volume{c}\" } },
            new() { ["DiskNumber"] = 0u, ["PartitionNumber"] = 2u, ["Offset"] = (ulong)(101 * MiB), ["Size"] = (ulong)(16 * MiB), ["GptType"] = "{e3c9e316-0b5c-4db8-817d-f92df00215ae}", ["Type"] = "Reserved", ["DriveLetter"] = '\0' },
        };
        var volumes = new List<WmiRow>
        {
            new() { ["Path"] = @"\\?\Volume{a}\", ["FileSystem"] = "FAT32", ["Size"] = (ulong)(100 * MiB), ["SizeRemaining"] = (ulong)(70 * MiB) },
            new() { ["Path"] = @"\\?\Volume{c}\", ["DriveLetter"] = 'C', ["FileSystem"] = "NTFS", ["FileSystemLabel"] = "Windows", ["Size"] = (ulong)(400 * GiB), ["SizeRemaining"] = (ulong)(150 * GiB) },
        };

        var result = PartitionReader.Assemble(disks, partitions, volumes, new HashSet<char> { 'C' }, 'C', 'C');
        Assert.Equal(2, result.Count);

        var ssd = result[0];
        Assert.Equal(PartitionStyle.Gpt, ssd.Style);
        Assert.True(ssd.IsSystemDisk);
        Assert.Equal("NVMe", ssd.Bus);
        Assert.Equal(new[] { 1, 2, 3 }, ssd.Partitions.Select(p => p.PartitionNumber));

        var efi = ssd.Partitions[0];
        Assert.Equal(PartitionKind.Efi, efi.Kind);
        Assert.True(efi.IsBootLoader);
        Assert.Null(efi.DriveLetter);
        Assert.Equal("FAT32", efi.FileSystem);

        var c = ssd.Partitions[2];
        Assert.Equal('C', c.DriveLetter);
        Assert.True(c.IsWindows);
        Assert.True(c.HasPageFile);
        Assert.True(c.HasThisApp);
        Assert.Equal("Windows", c.Label);
        Assert.Equal(250 * GiB, c.UsedBytes);

        var free = Assert.Single(ssd.FreeRegions);
        Assert.Equal(c.End, free.Offset);

        var usb = result[1];
        Assert.Equal(PartitionStyle.Raw, usb.Style);
        Assert.True(usb.IsRemovable);
        Assert.Empty(usb.FreeRegions);
    }

    [Fact]
    public void DynamicTranslationKeysExist()
    {
        var vi = Loc.Load(Loc.Vietnamese);
        foreach (var kind in Enum.GetValues<PartitionKind>()) Assert.True(vi.ContainsKey($"part.kind.{kind.ToString().ToLowerInvariant()}"), kind.ToString());
        Assert.True(vi.ContainsKey("part.kind.free"));
        foreach (var op in new[] { "shrink", "extend", "merge", "letter", "remove_letter", "label", "convert_ntfs", "check", "open", "format", "delete", "create" })
        {
            Assert.True(vi.ContainsKey($"part.action.{op}"), op);
        }

        foreach (var stage in new[] { "copy", "delete", "extend" }) Assert.True(vi.ContainsKey($"part.busy.merge_{stage}"), stage);
        foreach (var reason in new[] { "windows", "boot", "msr", "pagefile", "app" }) Assert.True(vi.ContainsKey($"part.block.{reason}_next"), reason);
    }
}

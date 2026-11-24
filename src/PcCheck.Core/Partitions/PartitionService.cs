using System.Globalization;
using System.Text;
using PcCheck.Core.Fixes;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Partitions;

/// <summary>
/// Thực hiện thao tác phân vùng bằng công cụ có sẵn của Windows (module Storage của PowerShell, robocopy, convert,
/// mbr2gpt, bcdboot). Không ghi trực tiếp lên ổ – Windows tự kiểm tra và từ chối thao tác không an toàn.
/// </summary>
public static class PartitionService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Đang có thao tác phân vùng chạy (chỉ cho chạy một thao tác mỗi lúc).</summary>
    public static bool IsRunning => Gate.CurrentCount == 0;

    public static Task<List<PhysicalDiskInfo>> ReadAsync() => Task.Run(PartitionReader.Read);

    public static async Task<PartitionSizeRange?> GetSupportedSizeAsync(int disk, int partition, CancellationToken ct = default)
    {
        var result = await PowerShellAsync(PartitionCommands.SupportedSize(disk, partition), ct).ConfigureAwait(false);
        return result.Success ? ParseSupportedSize(result.Detail) : null;
    }

    /// <summary>Đọc "min|max" do lệnh SupportedSize in ra.</summary>
    internal static PartitionSizeRange? ParseSupportedSize(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            var parts = line.Split('|');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
                && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)
                && min > 0 && max >= min)
            {
                return new PartitionSizeRange(min, max);
            }
        }

        return null;
    }

    public static Task<PartitionOpResult> ResizeAsync(int disk, int partition, long size, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.Resize(disk, partition, size), ct));

    public static Task<PartitionOpResult> CreateAsync(int disk, long offset, long size, char? letter, string fs, string label, bool full, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.Create(disk, offset, size, letter, fs, label, full), ct));

    public static Task<PartitionOpResult> DeleteAsync(int disk, int partition, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.Delete(disk, partition), ct));

    public static Task<PartitionOpResult> FormatAsync(int disk, int partition, string fs, string label, bool full, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.Format(disk, partition, fs, label, full), ct));

    public static Task<PartitionOpResult> ChangeLetterAsync(int disk, int partition, char? current, char letter, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.ChangeLetter(disk, partition, current, letter), ct));

    public static Task<PartitionOpResult> RemoveLetterAsync(int disk, int partition, char letter, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.RemoveLetter(disk, partition, letter), ct));

    public static Task<PartitionOpResult> SetLabelAsync(int disk, int partition, string fs, string label, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.SetLabel(disk, partition, fs, label), ct));

    public static Task<PartitionOpResult> InitializeAsync(int disk, PartitionStyle style, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.Initialize(disk, style), ct));

    public static Task<PartitionOpResult> ConvertEmptyAsync(int disk, PartitionStyle style, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.ConvertEmpty(disk, style), ct));

    public static Task<PartitionOpResult> SetOnlineAsync(int disk, CancellationToken ct = default) =>
        ExclusiveAsync(() => PowerShellAsync(PartitionCommands.SetOnline(disk), ct));

    /// <summary>
    /// Gộp: (1) chép dữ liệu của phân vùng bị gộp vào một thư mục trong phân vùng giữ lại (nếu chọn), (2) dời WinRE vào ổ
    /// Windows và xóa phân vùng khôi phục nằm giữa (nếu có), (3) xóa phân vùng bị gộp, (4) mở rộng phân vùng giữ lại chiếm
    /// toàn bộ vùng trống. Dừng ngay nếu bước chép lỗi – không xóa gì.
    /// </summary>
    public static Task<PartitionOpResult> MergeAsync(MergePlan plan, bool copyData, string folderPrefix, IProgress<string>? progress = null, CancellationToken ct = default) =>
        ExclusiveAsync(async () =>
        {
            var keep = plan.Keep;
            var removed = plan.Removed;
            if (copyData)
            {
                if (keep.DriveLetter is not { } target || removed.DriveLetter is not { } source) return new PartitionOpResult(false, "no drive letter");
                progress?.Report("copy");
                var folder = PartitionCommands.MergeFolderName(source, folderPrefix);
                var copy = await HiddenProcess.RunAsync(SystemExe("robocopy.exe"), PartitionCommands.MergeCopyArgs(source, target, folder), ct, oem: true).ConfigureAwait(false);
                Log.Info($"robocopy merge {source}: -> {target}:\\{folder}: exit {copy.ExitCode}\n{Tail(copy.Output, 2000)}");
                if (copy.Cancelled) return new PartitionOpResult(false, "cancelled");
                if (!PartitionCommands.RobocopySucceeded(copy.ExitCode)) return new PartitionOpResult(false, $"robocopy exit {copy.ExitCode}: {Tail(copy.Output, 400)}");
            }

            var winre = await RemoveRecoveryAsync(plan.Recoveries, progress, ct).ConfigureAwait(false);
            if (!winre.Success) return winre;

            progress?.Report("delete");
            var delete = await PowerShellAsync(PartitionCommands.Delete(removed.DiskNumber, removed.PartitionNumber), ct).ConfigureAwait(false);
            if (!delete.Success) return await RestoreWinReAsync(plan.Recoveries, delete, ct).ConfigureAwait(false);

            progress?.Report("extend");
            var extend = await PowerShellAsync(PartitionCommands.ExtendToMax(keep.DiskNumber, keep.PartitionNumber), ct).ConfigureAwait(false);
            return await RestoreWinReAsync(plan.Recoveries, extend, ct).ConfigureAwait(false);
        });

    /// <summary>Mở rộng qua phân vùng khôi phục đang chắn: dời WinRE vào ổ Windows, xóa phân vùng khôi phục, rồi mở rộng.</summary>
    public static Task<PartitionOpResult> ExtendOverRecoveryAsync(PartitionInfo partition, IReadOnlyList<PartitionInfo> recoveries, long size, IProgress<string>? progress = null, CancellationToken ct = default) =>
        ExclusiveAsync(async () =>
        {
            var winre = await RemoveRecoveryAsync(recoveries, progress, ct).ConfigureAwait(false);
            if (!winre.Success) return winre;

            progress?.Report("extend");
            var script = $"$s = Get-PartitionSupportedSize -DiskNumber {partition.DiskNumber.ToString(CultureInfo.InvariantCulture)} -PartitionNumber {partition.PartitionNumber.ToString(CultureInfo.InvariantCulture)}; " +
                         $"$size = [Math]::Min([long]{size.ToString(CultureInfo.InvariantCulture)}, [long]$s.SizeMax); " +
                         $"Resize-Partition -DiskNumber {partition.DiskNumber.ToString(CultureInfo.InvariantCulture)} -PartitionNumber {partition.PartitionNumber.ToString(CultureInfo.InvariantCulture)} -Size $size -Confirm:$false";
            var extend = await PowerShellAsync(script, ct).ConfigureAwait(false);
            return await RestoreWinReAsync(recoveries, extend, ct).ConfigureAwait(false);
        });

    /// <summary>
    /// reagentc /disable chép winre.wim từ phân vùng khôi phục vào C:\Windows\System32\Recovery, sau đó xóa phân vùng khôi phục.
    /// Nếu WinRE vốn không nằm ở đó (hoặc đã tắt), lệnh báo lỗi nhưng vẫn xóa tiếp – người dùng đã được cảnh báo.
    /// </summary>
    private static async Task<PartitionOpResult> RemoveRecoveryAsync(IReadOnlyList<PartitionInfo> recoveries, IProgress<string>? progress, CancellationToken ct)
    {
        if (recoveries.Count == 0) return new PartitionOpResult(true, "");
        progress?.Report("recovery");
        var disable = await HiddenProcess.RunAsync(SystemExe("reagentc.exe"), "/disable", ct, oem: true).ConfigureAwait(false);
        Log.Info($"reagentc /disable: exit {disable.ExitCode}\n{disable.Output.Trim()}");
        if (disable.Cancelled) return new PartitionOpResult(false, "cancelled");

        foreach (var r in recoveries.OrderByDescending(r => r.PartitionNumber))
        {
            var delete = await PowerShellAsync(PartitionCommands.Delete(r.DiskNumber, r.PartitionNumber), ct).ConfigureAwait(false);
            if (!delete.Success) return await RestoreWinReAsync(recoveries, delete, ct).ConfigureAwait(false);
        }

        return new PartitionOpResult(true, "");
    }

    /// <summary>Bật lại WinRE (giờ nằm trong ổ Windows) – lỗi ở bước này không làm hỏng kết quả chính, chỉ ghi chú thêm.</summary>
    private static async Task<PartitionOpResult> RestoreWinReAsync(IReadOnlyList<PartitionInfo> recoveries, PartitionOpResult result, CancellationToken ct)
    {
        if (recoveries.Count == 0) return result;
        var enable = await HiddenProcess.RunAsync(SystemExe("reagentc.exe"), "/enable", ct, oem: true).ConfigureAwait(false);
        Log.Info($"reagentc /enable: exit {enable.ExitCode}\n{enable.Output.Trim()}");
        return enable.ExitCode == 0 || !result.Success ? result : result with { Detail = WinReNotRestored };
    }

    /// <summary>Đánh dấu: thao tác xong nhưng không bật lại được WinRE.</summary>
    public const string WinReNotRestored = "winre";

    /// <summary>Chuyển FAT/FAT32/exFAT sang NTFS mà giữ nguyên dữ liệu (lệnh convert của Windows).</summary>
    public static Task<PartitionOpResult> ConvertToNtfsAsync(char letter, string currentLabel, CancellationToken ct = default) =>
        ExclusiveAsync(async () =>
        {
            var l = PartitionCommands.Letter(letter);
            if (!IsConsoleSafeLabel(currentLabel)) return new PartitionOpResult(false, "label");

            // convert hỏi nhãn hiện tại của ổ để xác nhận – trả lời qua đường ống.
            var echo = currentLabel.Length == 0 ? "echo." : "echo " + currentLabel;
            var run = await HiddenProcess.RunAsync(SystemExe("cmd.exe"), $"/d /c \"{echo}| \"{SystemExe("convert.exe")}\" {l}: /FS:NTFS /X /NoSecurity\"", ct, oem: true).ConfigureAwait(false);
            Log.Info($"convert {l}: /FS:NTFS: exit {run.ExitCode}\n{Tail(run.Output, 2000)}");
            return new PartitionOpResult(run.ExitCode == 0, Tail(run.Output, 400));
        });

    /// <summary>Nhãn chỉ gồm ký tự an toàn để gõ qua cmd (convert cần nhập lại nhãn).</summary>
    internal static bool IsConsoleSafeLabel(string label) => label.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_');

    /// <summary>Kiểm tra ổ hệ thống có chuyển MBR → GPT được không (mbr2gpt /validate).</summary>
    public static Task<PartitionOpResult> Mbr2GptValidateAsync(int disk, CancellationToken ct = default) =>
        ExclusiveAsync(() => Mbr2GptAsync(disk, "validate", ct));

    /// <summary>Chuyển ổ hệ thống MBR → GPT giữ nguyên dữ liệu (mbr2gpt /convert). Sau đó phải bật UEFI trong BIOS.</summary>
    public static Task<PartitionOpResult> Mbr2GptConvertAsync(int disk, CancellationToken ct = default) =>
        ExclusiveAsync(() => Mbr2GptAsync(disk, "convert", ct));

    private static async Task<PartitionOpResult> Mbr2GptAsync(int disk, string mode, CancellationToken ct)
    {
        var run = await HiddenProcess.RunAsync(SystemExe("mbr2gpt.exe"), $"/{mode} /disk:{disk.ToString(CultureInfo.InvariantCulture)} /allowFullOS", ct, oem: true).ConfigureAwait(false);
        Log.Info($"mbr2gpt /{mode} disk {disk}: exit {run.ExitCode}\n{Tail(run.Output, 3000)}");
        return new PartitionOpResult(run.ExitCode == 0, Tail(run.Output, 400));
    }

    /// <summary>Tạo lại tệp khởi động Windows trên phân vùng EFI / System Reserved (bcdboot).</summary>
    public static Task<PartitionOpResult> RepairBootAsync(CancellationToken ct = default) =>
        ExclusiveAsync(async () =>
        {
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var run = await HiddenProcess.RunAsync(SystemExe("bcdboot.exe"), $"\"{windir}\"", ct, oem: true).ConfigureAwait(false);
            Log.Info($"bcdboot {windir}: exit {run.ExitCode}\n{run.Output.Trim()}");
            return new PartitionOpResult(run.ExitCode == 0, Tail(run.Output, 400));
        });

    private static string SystemExe(string name) => Path.Combine(Environment.SystemDirectory, name);

    private static async Task<PartitionOpResult> ExclusiveAsync(Func<Task<PartitionOpResult>> work)
    {
        if (!await Gate.WaitAsync(0).ConfigureAwait(false)) return new PartitionOpResult(false, "busy");
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Thao tác phân vùng lỗi", ex);
            return new PartitionOpResult(false, ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Chạy lệnh PowerShell (mã hóa base64 – không phải lo dấu nháy), lỗi được in ra và trả mã thoát 1.</summary>
    private static async Task<PartitionOpResult> PowerShellAsync(string script, CancellationToken ct)
    {
        var wrapped = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
                      "try { " + script + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
        var run = await HiddenProcess.RunAsync(SystemExe(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, ct).ConfigureAwait(false);
        Log.Info($"Partition PS: {script}\nexit {run.ExitCode}\n{Tail(run.Output, 2000)}");
        if (run.Cancelled) return new PartitionOpResult(false, "cancelled");
        return new PartitionOpResult(run.ExitCode == 0, run.ExitCode == 0 ? run.Output.Trim() : Tail(run.Output, 400));
    }

    /// <summary>Phần cuối của đầu ra (bỏ dòng trống), giới hạn độ dài.</summary>
    internal static string Tail(string output, int max)
    {
        var text = string.Join("\n", output.Split('\n').Select(l => l.TrimEnd('\r', ' ')).Where(l => l.Trim().Length > 0)).Trim();
        return text.Length <= max ? text : "…" + text[^max..];
    }
}

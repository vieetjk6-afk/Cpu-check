using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using PcCheck.Core.Util;

namespace PcCheck.Core.Cleanup;

public sealed record LeftoverRemoval(int Removed, int Failed, long FreedBytes, string BackupFolder);

/// <summary>
/// Xóa phần còn sót đã được người dùng chọn. Tệp/thư mục/lối tắt: chuyển vào Thùng rác (khôi phục được).
/// Khóa/giá trị registry: xuất ra tệp .reg trước khi xóa (nhấp đúp để khôi phục). Tác vụ hẹn giờ: lưu bản XML.
/// Dịch vụ: chỉ những dịch vụ mà tệp chạy đã không còn (sc delete).
/// </summary>
public static class LeftoverRemover
{
    public static string BackupRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcCheck", "Backups");

    public static async Task<LeftoverRemoval> RemoveAsync(IReadOnlyList<Leftover> items, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var backup = Path.Combine(BackupRoot, "Uninstall-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backup);
        int removed = 0, failed = 0, index = 0;
        long freed = 0;
        var history = new List<Models.OperationRecord>();

        // Xóa dịch vụ/tác vụ/registry trước, tệp sau (tệp có thể đang bị dịch vụ giữ).
        foreach (var item in items.OrderBy(i => Order(i.Kind)))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(item.Path);
            index++;
            bool ok;
            try
            {
                ok = item.Kind switch
                {
                    LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut => await RecycleAsync(item).ConfigureAwait(false),
                    LeftoverKind.RegistryKey => await DeleteKeyAsync(item.Path, Path.Combine(backup, $"{index:D3}-key.reg"), ct).ConfigureAwait(false),
                    LeftoverKind.RegistryValue => await DeleteValueAsync(item.Path, item.ValueName, Path.Combine(backup, $"{index:D3}-run.reg"), ct).ConfigureAwait(false),
                    LeftoverKind.ScheduledTask => await DeleteTaskAsync(item.Path, Path.Combine(backup, $"{index:D3}-task.xml"), ct).ConfigureAwait(false),
                    LeftoverKind.Service => await RunAsync("sc.exe", $"delete \"{item.Path}\"", ct).ConfigureAwait(false) == 0,
                    _ => false,
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn($"Xóa phần còn sót {item.Kind} {item.Path} lỗi", ex);
                ok = false;
            }

            if (ok)
            {
                removed++;
                freed += item.Bytes;
            }
            else
            {
                failed++;
            }

            var isFile = item.Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut;
            history.Add(new Models.OperationRecord
            {
                Time = DateTime.Now,
                Source = Storage.OperationLog.SourceLeftovers,
                Action = !ok ? Storage.OperationLog.ActionFailed : isFile ? Storage.OperationLog.ActionRecycled : Storage.OperationLog.ActionRemoved,
                Target = item.Kind == LeftoverKind.RegistryValue ? $"{item.Path}\\{item.ValueName}" : item.Path,
                Bytes = ok ? item.Bytes : 0,
                Detail = item.Program,
            });
        }

        Storage.OperationLog.Record(history);

        // Không có gì cần sao lưu -> bỏ thư mục trống.
        try
        {
            if (!Directory.EnumerateFileSystemEntries(backup).Any()) Directory.Delete(backup);
        }
        catch (IOException)
        {
        }

        Log.Info($"Xóa phần còn sót: {removed} ok, {failed} lỗi, {freed} byte");
        return new LeftoverRemoval(removed, failed, freed, Directory.Exists(backup) ? backup : "");
    }

    internal static int Order(LeftoverKind kind) => kind switch
    {
        LeftoverKind.Service => 0,
        LeftoverKind.ScheduledTask => 1,
        LeftoverKind.RegistryValue => 2,
        LeftoverKind.RegistryKey => 3,
        LeftoverKind.Shortcut => 4,
        LeftoverKind.File => 5,
        _ => 6,
    };

    // ===== Thùng rác (SHFileOperation, FOF_ALLOWUNDO) =====

    private static Task<bool> RecycleAsync(Leftover item) => RecycleBin.MoveAsync(item.Path, item.Bytes);

    // ===== Registry =====

    private static (RegistryKey Root, string SubKey) Open(string fullPath)
    {
        var slash = fullPath.IndexOf('\\');
        var hive = fullPath[..slash].ToUpperInvariant();
        var sub = fullPath[(slash + 1)..];
        var root = hive is "HKCU" or "HKEY_CURRENT_USER"
            ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
            : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); // đường dẫn đã ghi rõ WOW6432Node nếu là 32-bit
        return (root, sub);
    }

    private static async Task<bool> DeleteKeyAsync(string fullPath, string backupFile, CancellationToken ct)
    {
        if (!LeftoverRules.IsSafeRegistryKey(fullPath[(fullPath.IndexOf('\\') + 1)..]) && !fullPath.Contains(@"\CurrentVersion\Uninstall\", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn($"Từ chối xóa khóa không an toàn: {fullPath}");
            return false;
        }

        if (await RunAsync("reg.exe", $"export \"{fullPath}\" \"{backupFile}\" /y", ct).ConfigureAwait(false) != 0) return false; // không sao lưu được -> không xóa
        var (root, sub) = Open(fullPath);
        using (root)
        {
            root.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
        }

        return true;
    }

    private static async Task<bool> DeleteValueAsync(string keyPath, string valueName, string backupFile, CancellationToken ct)
    {
        if (await RunAsync("reg.exe", $"export \"{keyPath}\" \"{backupFile}\" /y", ct).ConfigureAwait(false) != 0) return false;
        var (root, sub) = Open(keyPath);
        using (root)
        using (var key = root.OpenSubKey(sub, writable: true))
        {
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }

        return true;
    }

    private static async Task<bool> DeleteTaskAsync(string taskPath, string backupFile, CancellationToken ct)
    {
        var xml = Path.Combine(Environment.SystemDirectory, "Tasks") + taskPath;
        if (File.Exists(xml)) File.Copy(xml, backupFile, overwrite: true);
        return await RunAsync("schtasks.exe", $"/Delete /TN \"{taskPath}\" /F", ct).ConfigureAwait(false) == 0;
    }

    private static async Task<int> RunAsync(string file, string args, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (process is null) return -1;
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        if (process.ExitCode != 0) Log.Warn($"{file} {args} -> {process.ExitCode}: {(await output.ConfigureAwait(false) + await error.ConfigureAwait(false)).Trim()}");
        return process.ExitCode;
    }
}

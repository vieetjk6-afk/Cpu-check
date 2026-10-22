using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PcCheck.Core.Fixes;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Cleanup;

public enum UninstallResult
{
    /// <summary>Đã gỡ: mục gỡ cài đặt không còn trong registry.</summary>
    Removed,

    /// <summary>Trình gỡ đã đóng nhưng phần mềm vẫn còn (người dùng hủy, hoặc cần khởi động lại).</summary>
    StillInstalled,

    /// <summary>Không chạy được trình gỡ.</summary>
    Failed,
}

/// <summary>
/// Chạy trình gỡ cài đặt và chờ tới khi phần mềm thật sự biến mất khỏi danh sách (nhiều trình gỡ – Inno Setup, NSIS – tự chép
/// sang thư mục Temp rồi chạy tiếp, nên tiến trình ban đầu thoát ngay).
/// </summary>
public static partial class Uninstaller
{
    private static readonly TimeSpan AfterExitWait = TimeSpan.FromMinutes(3);

    /// <summary>Trình gỡ báo lỗi/bị hủy (mã thoát khác 0): chỉ chờ ngắn phòng khi gỡ vẫn đang hoàn tất.</summary>
    private static readonly TimeSpan AfterErrorWait = TimeSpan.FromSeconds(15);

    /// <summary>Mã thoát msiexec coi là thành công: 0, 1605 (sản phẩm không còn), 1641/3010 (xong, cần khởi động lại).</summary>
    private static readonly HashSet<int> MsiSuccess = [0, 1605, 1641, 3010];

    /// <summary>Lệnh gỡ: im lặng khi có thể (QuietUninstallString / MSI /passive), nếu không thì trình gỡ bình thường.</summary>
    internal static (string File, string Args, bool Quiet) PlanCommand(InstalledProgram program, bool preferQuiet)
    {
        var msi = MsiGuidRegex().Match(program.UninstallCommand);
        if (msi.Success && program.UninstallCommand.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            // /passive: chỉ hiện thanh tiến trình, không hỏi; /norestart: không tự khởi động lại máy.
            return preferQuiet
                ? ("msiexec.exe", $"/x {msi.Value} /passive /norestart", true)
                : ("msiexec.exe", $"/x {msi.Value}", false);
        }

        var command = preferQuiet && program.QuietUninstallCommand.Trim().Length > 0 ? program.QuietUninstallCommand : program.UninstallCommand;
        var (exe, args) = FixService.SplitCommand(command);
        return (exe, args, preferQuiet && program.QuietUninstallCommand.Trim().Length > 0);
    }

    public static async Task<UninstallResult> RunAsync(InstalledProgram program, bool preferQuiet, IProgress<string>? stage = null, CancellationToken ct = default)
    {
        if (program.Scope == ProgramScope.Store)
        {
            stage?.Report("running");
            var outcome = await FixService.UninstallAsync(program, ct).ConfigureAwait(false);
            return outcome.Success ? UninstallResult.Removed : UninstallResult.Failed;
        }

        var (file, args, _) = PlanCommand(program, preferQuiet);
        if (file.Length == 0) return UninstallResult.Failed;

        stage?.Report("running");
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, WorkingDirectory = SafeDirectory(file) });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Warn($"Chạy trình gỡ {program.Name} lỗi", ex);
            return UninstallResult.Failed;
        }

        int? exitCode = null;
        if (process is not null)
        {
            using (process)
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                exitCode = process.ExitCode;
                Log.Info($"Trình gỡ {program.Name} thoát với mã {exitCode}");
            }
        }

        // msiexec báo rõ kết quả (1602 hủy, 1603 lỗi, 1618 đang có cài đặt khác...) -> không cần chờ.
        if (exitCode is { } code && Path.GetFileName(file).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) && !MsiSuccess.Contains(code))
        {
            return IsInstalled(program) ? UninstallResult.StillInstalled : UninstallResult.Removed;
        }

        // Chờ mục gỡ cài đặt biến mất (trình gỡ có thể vẫn đang chạy ở tiến trình con).
        stage?.Report("waiting");
        var deadline = DateTime.UtcNow + (exitCode is null or 0 ? AfterExitWait : AfterErrorWait);
        while (IsInstalled(program))
        {
            if (DateTime.UtcNow > deadline) return UninstallResult.StillInstalled;
            await Task.Delay(2000, ct).ConfigureAwait(false);
        }

        return UninstallResult.Removed;
    }

    private static string SafeDirectory(string file)
    {
        try
        {
            var dir = Path.GetDirectoryName(file);
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : Environment.SystemDirectory;
        }
        catch (ArgumentException)
        {
            return Environment.SystemDirectory;
        }
    }

    /// <summary>
    /// Mục gỡ cài đặt của phần mềm (Id dạng "LocalMachine|Registry64|tên khóa") còn tồn tại không.
    /// Không đọc được -> coi như CÒN cài, để không bao giờ quét/xóa phần còn sót của phần mềm chưa gỡ xong.
    /// </summary>
    public static bool IsInstalled(InstalledProgram program)
    {
        var parts = program.Id.Split('|');
        if (parts.Length != 3 || !Enum.TryParse<RegistryHive>(parts[0], out var hive) || !Enum.TryParse<RegistryView>(parts[1], out var view)) return true;
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{parts[2]}");
            return key?.GetValue("DisplayName") is string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Không đọc được mục gỡ cài đặt của {program.Name}", ex);
            return true;
        }
    }

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex MsiGuidRegex();
}

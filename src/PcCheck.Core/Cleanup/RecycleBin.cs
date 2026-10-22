using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PcCheck.Core.Util;

namespace PcCheck.Core.Cleanup;

/// <summary>
/// Kiểm tra Thùng rác có giữ được một tệp/thư mục không. SHFileOperation với FOF_ALLOWUNDO + không hỏi sẽ XÓA VĨNH VIỄN
/// mà không báo khi: ổ không có Thùng rác (USB, thẻ nhớ, ổ mạng), Thùng rác bị tắt (NukeOnDelete / chính sách NoRecycleFiles),
/// hoặc mục lớn hơn dung lượng tối đa của Thùng rác trên ổ đó.
/// </summary>
public static class RecycleBin
{
    private const string BitBucket = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket";
    private const string ExplorerPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    public static bool CanHold(string path, long bytes, out string reason)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                reason = "network path";
                return false;
            }

            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed)
            {
                reason = $"drive type {drive.DriveType}";
                return false;
            }

            if (PolicyFlag("NoRecycleFiles"))
            {
                reason = "recycle bin disabled by policy";
                return false;
            }

            var (maxBytes, nuke) = Settings(root, drive.TotalSize);
            if (nuke)
            {
                reason = "recycle bin disabled on this drive";
                return false;
            }

            if (bytes >= maxBytes)
            {
                reason = $"{bytes} bytes > recycle bin size {maxBytes}";
                return false;
            }

            reason = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            Log.Warn($"Kiểm tra Thùng rác cho {path} lỗi", ex);
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>Dung lượng tối đa (byte) và có tắt Thùng rác trên ổ này không. Không đọc được cấu hình -> mặc định 5% dung lượng ổ.</summary>
    private static (long MaxBytes, bool Nuke) Settings(string root, long driveSize)
    {
        var fallback = driveSize / 20;
        var policyPercent = PolicyValue("RecycleBinSize");
        if (policyPercent is > 0 and <= 100) fallback = driveSize / 100 * policyPercent.Value;

        var guid = VolumeGuid(root);
        if (guid is null) return (fallback, false);
        using var key = Registry.CurrentUser.OpenSubKey($@"{BitBucket}\Volume\{guid}");
        if (key is null) return (fallback, false);
        var nuke = key.GetValue("NukeOnDelete") is int n && n != 0;
        var max = key.GetValue("MaxCapacity") is int mb && mb > 0 ? mb * 1024L * 1024L : fallback;
        return (max, nuke);
    }

    private static bool PolicyFlag(string name) => PolicyValue(name) is { } v && v != 0;

    private static int? PolicyValue(string name)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(ExplorerPolicies);
            if (key?.GetValue(name) is int value) return value;
        }

        return null;
    }

    /// <summary>"C:\" -> "{GUID}" của ổ (khóa BitBucket\Volume\{GUID}).</summary>
    private static string? VolumeGuid(string root)
    {
        var buffer = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPoint(root.EndsWith('\\') ? root : root + "\\", buffer, (uint)buffer.Capacity)) return null;
        var name = buffer.ToString(); // \\?\Volume{xxxxxxxx-...}\
        var start = name.IndexOf('{');
        var end = name.IndexOf('}');
        return start >= 0 && end > start ? name[start..(end + 1)] : null;
    }

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    private static extern bool GetVolumeNameForVolumeMountPoint(string volumeMountPoint, StringBuilder volumeName, uint bufferLength);
#pragma warning restore SYSLIB1054
}

using System.Security.AccessControl;
using System.Security.Principal;

namespace PcCheck.Core.Util;

/// <summary>
/// Thư mục tạm cho tệp sẽ được chạy với quyền admin (trình cài driver, bản cập nhật). Khi ứng dụng đang có quyền admin:
/// thư mục riêng trong ProgramData chỉ Administrators / SYSTEM ghi được, để tiến trình thường của người dùng không thể
/// tráo tệp trước khi nó chạy. Chưa có quyền admin thì dùng %TEMP% (Windows sẽ hỏi UAC khi chạy).
/// </summary>
public static class SecureTemp
{
    public static string CreateDirectory(string prefix)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}";
        if (!OperatingSystem.IsWindows() || !Elevation.IsAdministrator())
        {
            var temp = Path.Combine(Path.GetTempPath(), "PcCheck", name);
            Directory.CreateDirectory(temp);
            return temp;
        }

        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PcCheck");
        Directory.CreateDirectory(parent);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        var path = Path.Combine(parent, name);
        new DirectoryInfo(path).Create(security);
        return path;
    }

    public static void Delete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

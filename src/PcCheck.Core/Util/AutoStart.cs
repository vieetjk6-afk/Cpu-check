using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace PcCheck.Core.Util;

/// <summary>
/// Khởi động widget cùng Windows bằng Task Scheduler (chạy quyền cao nhất, không hiện hộp thoại UAC).
/// Không dùng khóa Run vì ứng dụng yêu cầu quyền quản trị – Windows chặn yêu cầu nâng quyền lúc đăng nhập.
/// </summary>
public static class AutoStart
{
    public const string TaskName = "PC Check Widget";
    public const string WidgetArgument = "--widget";

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Enable(string executablePath)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"pccheck-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(executablePath, CurrentUser()), Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F") == 0;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (IOException)
            {
            }
        }
    }

    public static bool Disable() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

    private static string CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    internal static string BuildTaskXml(string executablePath, string user)
    {
        var exe = SecurityElement.Escape(executablePath);
        var userId = SecurityElement.Escape(user);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>PC Check – widget theo dõi hệ thống</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{userId}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{userId}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <Arguments>{WidgetArgument}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return -1;
            process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            if (process.ExitCode != 0 && !arguments.StartsWith("/Query", StringComparison.Ordinal))
            {
                Log.Warn($"schtasks {arguments} -> {process.ExitCode}: {error.Trim()}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Warn("Chạy schtasks lỗi", ex);
            return -1;
        }
    }
}

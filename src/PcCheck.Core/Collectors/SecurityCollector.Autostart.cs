using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using PcCheck.Core.Analysis;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

public static partial class SecurityCollector
{
    private const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

    private static List<AutostartEntry> ReadAutostart()
    {
        var list = new List<AutostartEntry>();

        // Khóa Run + thư mục Startup: dùng lại StartupCollector (có cả trạng thái bật/tắt kiểu Task Manager).
        foreach (var item in StartupCollector.Collect())
        {
            var folder = item.Location is StartupLocation.UserStartupFolder or StartupLocation.CommonStartupFolder;
            var command = item.Command;
            var unresolved = false;

            // Lối tắt (.lnk) trong thư mục Startup: đánh giá chương trình đích chứ không phải tệp lối tắt
            // (tệp .lnk không bao giờ có chữ ký số, nằm trong thư mục người dùng – sẽ bị báo sai).
            if (folder && command.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (ResolveShortcut(command) is { Target.Length: > 0 } link)
                    command = $"\"{link.Target}\"" + (link.Arguments.Length > 0 ? " " + link.Arguments : "");
                else
                    unresolved = true;
            }

            list.Add(new AutostartEntry
            {
                Source = folder ? AutostartSource.StartupFolder : AutostartSource.RunKey,
                Name = item.Name,
                Command = command,
                Location = item.Location.ToString(),
                Enabled = item.Enabled,
                ActionId = item.Id,

                // Không đọc được đích (lối tắt kiểu MSI "advertised"): không có tệp để đánh giá.
                Target = unresolved ? new FileTrust("", true, null, "", "", PathKind.Unknown) : null,
            });
        }

        foreach (var (hive, view, label) in new[]
                 {
                     (RegistryHive.CurrentUser, RegistryView.Default, @"HKCU\" + RunOncePath),
                     (RegistryHive.LocalMachine, RegistryView.Registry64, @"HKLM\" + RunOncePath),
                     (RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\WOW6432Node\" + RunOncePath),
                 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(RunOncePath);
                if (key is null) continue;
                foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
                {
                    list.Add(new AutostartEntry { Source = AutostartSource.RunOnce, Name = name, Command = key.GetValue(name)?.ToString() ?? "", Location = label });
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }

        list.AddRange(ReadScheduledTasks());

        foreach (var entry in list.Where(e => e.Target is null))
        {
            entry.Flags = SecurityCatalog.AnalyzeCommand(entry.Command);
            var exe = ResolveExecutable(entry.Command);

            // Chạy qua trình thông dịch: tệp đáng xét là script trong tham số (vd wscript.exe C:\Users\...\a.vbs).
            if (entry.Flags.HasFlag(CommandFlags.ScriptHost) && SecurityCatalog.ScriptPathOf(entry.Command) is { } script) exe = script;
            entry.Target = exe.Length > 0 ? new FileTrust(exe, true, null, "", "", SecurityCatalog.Classify(exe)) : null;
        }

        Parallel.ForEach(list.Where(e => e.Target is { Path.Length: > 0 }), new ParallelOptions { MaxDegreeOfParallelism = 4 },
            e => e.Target = TrustChecker.Check(e.Target!.Path));
        return list;
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
    }

    /// <summary>Đích và tham số của lối tắt .lnk (qua ShellLink của Windows).</summary>
    private static (string Target, string Arguments)? ResolveShortcut(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        object? link = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
            if (type is null) return null;
            link = Activator.CreateInstance(type);
            if (link is not IShellLinkW shell || link is not IPersistFile file) return null;
            file.Load(path, 0);
            var target = new StringBuilder(1024);
            shell.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(4096);
            shell.GetArguments(args, args.Capacity);
            return (Environment.ExpandEnvironmentVariables(target.ToString()), args.ToString());
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"Đọc lối tắt {path} lỗi", ex);
            return null;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Tệp thực thi của lệnh; tên trần (vd "rundll32.exe") được tìm trong System32.</summary>
    private static string ResolveExecutable(string command)
    {
        var exe = SecurityCatalog.ExecutableOf(command);
        if (exe.Length == 0 || Path.IsPathRooted(exe)) return exe;
        var system = Path.Combine(Environment.SystemDirectory, exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe");
        return File.Exists(system) ? system : exe;
    }

    /// <summary>
    /// Tác vụ hẹn giờ: đọc tệp XML trong %SystemRoot%\System32\Tasks. Bỏ qua tác vụ của Windows (thư mục \Microsoft\
    /// chạy tệp trong thư mục Windows) – trừ khi lệnh có dấu hiệu bất thường.
    /// </summary>
    private static List<AutostartEntry> ReadScheduledTasks()
    {
        var list = new List<AutostartEntry>();
        var root = Path.Combine(Environment.SystemDirectory, "Tasks");
        if (!Directory.Exists(root)) return list;

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return list;
        }

        foreach (var file in files)
        {
            var taskPath = file[root.Length..].Replace('/', '\\');
            XDocument doc;
            try
            {
                doc = XDocument.Load(file);
            }
            catch (Exception)
            {
                continue;
            }

            var elements = doc.Descendants().ToList();
            var settings = elements.FirstOrDefault(e => e.Name.LocalName == "Settings");
            var enabled = !string.Equals(settings?.Elements().FirstOrDefault(e => e.Name.LocalName == "Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase);
            var hidden = string.Equals(settings?.Elements().FirstOrDefault(e => e.Name.LocalName == "Hidden")?.Value, "true", StringComparison.OrdinalIgnoreCase);

            foreach (var exec in elements.Where(e => e.Name.LocalName == "Exec"))
            {
                var command = exec.Elements().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value.Trim() ?? "";
                if (command.Length == 0) continue;
                var arguments = exec.Elements().FirstOrDefault(e => e.Name.LocalName == "Arguments")?.Value.Trim() ?? "";
                var full = (command.Contains(' ') && !command.StartsWith('"') ? $"\"{command}\"" : command) + (arguments.Length > 0 ? " " + arguments : "");

                var flags = SecurityCatalog.AnalyzeCommand(full);
                var exe = ResolveExecutable(full);
                var inWindows = SecurityCatalog.Classify(exe) == PathKind.Windows;
                var microsoftTask = taskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
                var unusual = flags.HasFlag(CommandFlags.EncodedCommand) || flags.HasFlag(CommandFlags.Downloads) || flags.HasFlag(CommandFlags.HiddenWindow);
                if (microsoftTask && inWindows && !unusual) continue;

                // Tác vụ chạy công cụ Windows (vd rundll32) với tham số bình thường của phần mềm khác: vẫn liệt kê, phân tích bên dưới.
                list.Add(new AutostartEntry
                {
                    Source = AutostartSource.ScheduledTask,
                    Name = Path.GetFileName(taskPath),
                    Command = full,
                    Location = taskPath,
                    Enabled = enabled,
                    Hidden = hidden,
                    ActionId = taskPath,
                });
            }
        }

        return list;
    }

    // ===== Chiếm quyền khởi chạy (IFEO, Winlogon, AppInit) =====

    private static List<HijackEntry> ReadHijacks()
    {
        var list = new List<HijackEntry>();
        const string ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using (var key = root.OpenSubKey(ifeo))
            {
                foreach (var name in key?.GetSubKeyNames() ?? [])
                {
                    try
                    {
                        using var sub = key!.OpenSubKey(name);
                        if (sub?.GetValue("Debugger") is string debugger && debugger.Trim().Length > 0)
                            list.Add(new HijackEntry(HijackKind.IfeoDebugger, name, debugger.Trim(), $@"HKLM\{ifeo}\{name}"));
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                    {
                    }
                }
            }

            const string windows = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";
            using (var key = root.OpenSubKey(windows))
            {
                var dlls = key?.GetValue("AppInit_DLLs") as string ?? "";
                var load = key?.GetValue("LoadAppInit_DLLs") is int l && l != 0;
                if (load && dlls.Trim().Length > 0) list.Add(new HijackEntry(HijackKind.AppInitDlls, "AppInit_DLLs", dlls.Trim(), $@"HKLM\{windows}"));
            }
        }

        const string winlogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        using (var key = Registry.LocalMachine.OpenSubKey(winlogon))
        {
            if (key?.GetValue("Shell") is string shell) list.Add(new HijackEntry(HijackKind.WinlogonShell, "Shell", shell, $@"HKLM\{winlogon}"));
            if (key?.GetValue("Userinit") is string userinit) list.Add(new HijackEntry(HijackKind.WinlogonUserinit, "Userinit", userinit, $@"HKLM\{winlogon}"));
        }

        // Shell riêng cho người dùng hiện tại (thủ thuật thay explorer.exe chỉ với một tài khoản).
        using (var key = Registry.CurrentUser.OpenSubKey(winlogon))
        {
            if (key?.GetValue("Shell") is string shell && shell.Trim().Length > 0)
                list.Add(new HijackEntry(HijackKind.WinlogonShell, "Shell", shell, $@"HKCU\{winlogon}"));
        }

        return list;
    }
}

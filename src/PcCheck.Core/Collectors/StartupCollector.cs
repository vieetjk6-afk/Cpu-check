using System.Diagnostics;
using Microsoft.Win32;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

/// <summary>Chương trình khởi động cùng Windows; bật/tắt giống Task Manager (khóa StartupApproved).</summary>
public static class StartupCollector
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupItem> Collect()
    {
        var items = new List<StartupItem>();
        try
        {
            ReadRun(Registry.CurrentUser, RegistryView.Default, StartupLocation.UserRun, items);
            ReadRun(Registry.LocalMachine, RegistryView.Registry64, StartupLocation.MachineRun, items);
            ReadRun(Registry.LocalMachine, RegistryView.Registry32, StartupLocation.MachineRun32, items);
            ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLocation.UserStartupFolder, items);
            ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.CommonStartupFolder, items);
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc danh sách khởi động lỗi", ex);
        }

        return items.OrderByDescending(i => i.Enabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadRun(RegistryKey hiveKey, RegistryView view, StartupLocation location, List<StartupItem> items)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hiveKey == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view);
        using var run = baseKey.OpenSubKey(RunPath);
        if (run is null) return;
        foreach (var name in run.GetValueNames())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var command = run.GetValue(name)?.ToString() ?? "";
            items.Add(new StartupItem
            {
                Id = MakeId(location, name),
                Name = name,
                Command = command,
                Location = location,
                Enabled = IsApproved(location, name),
                Publisher = PublisherOf(command),
            });
        }
    }

    private static void ReadFolder(string folder, StartupLocation location, List<StartupItem> items)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new StartupItem
            {
                Id = MakeId(location, fileName),
                Name = Path.GetFileNameWithoutExtension(fileName),
                Command = file,
                Location = location,
                Enabled = IsApproved(location, fileName),
            });
        }
    }

    private static string MakeId(StartupLocation location, string valueName) => $"{(int)location}|{valueName}";

    private static (RegistryKey Hive, string SubKey) ApprovedKey(StartupLocation location) => location switch
    {
        StartupLocation.UserRun => (Registry.CurrentUser, ApprovedRoot + "Run"),
        StartupLocation.MachineRun => (Registry.LocalMachine, ApprovedRoot + "Run"),
        StartupLocation.MachineRun32 => (Registry.LocalMachine, ApprovedRoot + "Run32"),
        StartupLocation.UserStartupFolder => (Registry.CurrentUser, ApprovedRoot + "StartupFolder"),
        _ => (Registry.LocalMachine, ApprovedRoot + "StartupFolder"),
    };

    private static bool IsApproved(StartupLocation location, string valueName)
    {
        try
        {
            var (hive, sub) = ApprovedKey(location);
            using var key = hive.OpenSubKey(sub);
            if (key?.GetValue(valueName) is byte[] { Length: > 0 } data) return IsEnabledFlag(data[0]);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
        }

        return true;
    }

    /// <summary>Byte đầu chẵn (02, 06) = bật; lẻ (03, 07) = đã tắt.</summary>
    internal static bool IsEnabledFlag(byte flag) => (flag & 1) == 0;

    public static void SetEnabled(StartupItem item, bool enabled)
    {
        var separator = item.Id.IndexOf('|');
        var valueName = separator >= 0 ? item.Id[(separator + 1)..] : item.Name;
        var (hive, sub) = ApprovedKey(item.Location);
        using var key = hive.CreateSubKey(sub, writable: true);
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
        key.SetValue(valueName, data, RegistryValueKind.Binary);
        item.Enabled = enabled;
    }

    internal static string ExtractExecutable(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : command.Trim('"');
        }

        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? command[..(exe + 4)] : command.Split(' ')[0];
    }

    private static string PublisherOf(string command)
    {
        try
        {
            var exe = ExtractExecutable(command);
            if (File.Exists(exe)) return FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return "";
    }
}

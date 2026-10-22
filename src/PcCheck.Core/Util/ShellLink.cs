using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace PcCheck.Core.Util;

/// <summary>Đọc lối tắt .lnk (đích + tham số) qua ShellLink của Windows.</summary>
public static class ShellLink
{
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
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
    public static (string Target, string Arguments)? Resolve(string path)
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
}

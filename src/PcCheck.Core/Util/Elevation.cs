namespace PcCheck.Core.Util;

public static class Elevation
{
    /// <summary>Tiến trình hiện tại có đang chạy với quyền quản trị không.</summary>
    public static bool IsAdministrator() => NativeMethods.IsAdministrator();
}

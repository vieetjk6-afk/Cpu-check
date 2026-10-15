using PcCheck.Core.Localization;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>Ý nghĩa mã lỗi Device Manager (ConfigManagerErrorCode).</summary>
public static class DeviceErrorCodes
{
    public const int NotConnected = 45;
    public const int Disabled = 22;
    public const int DriversNotInstalled = 28;

    public static readonly IReadOnlyList<int> KnownCodes = [1, 3, 10, 12, 14, 18, 19, 21, 22, 24, 28, 29, 31, 32, 37, 38, 39, 40, 41, 43, 48, 52];

    private static readonly HashSet<int> MissingDriverCodes = [1, 18, 28, 31, 32, 37, 39, 40, 41, 52];

    public static LocText Describe(int code) =>
        KnownCodes.Contains(code) ? L($"devcode.{code}") : L("devcode.other", code);

    public static bool MeansMissingDriver(int code) => MissingDriverCodes.Contains(code);
}

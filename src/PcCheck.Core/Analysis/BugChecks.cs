namespace PcCheck.Core.Analysis;

/// <summary>Mã màn hình xanh phổ biến -> tên + nhóm nguyên nhân thường gặp (khóa gợi ý "bugcheck.hint.*").</summary>
public static class BugChecks
{
    public const string HintRam = "ram";
    public const string HintDriver = "driver";
    public const string HintGpu = "gpu";
    public const string HintDisk = "disk";
    public const string HintHardware = "hardware";
    public const string HintSystem = "system";

    public static readonly IReadOnlyList<string> AllHints = [HintRam, HintDriver, HintGpu, HintDisk, HintHardware, HintSystem];

    private static readonly Dictionary<uint, (string Name, string Hint)> Known = new()
    {
        [0x0000000A] = ("IRQL_NOT_LESS_OR_EQUAL", HintDriver),
        [0x0000001A] = ("MEMORY_MANAGEMENT", HintRam),
        [0x0000001E] = ("KMODE_EXCEPTION_NOT_HANDLED", HintDriver),
        [0x00000019] = ("BAD_POOL_HEADER", HintRam),
        [0x0000003B] = ("SYSTEM_SERVICE_EXCEPTION", HintDriver),
        [0x0000004E] = ("PFN_LIST_CORRUPT", HintRam),
        [0x00000050] = ("PAGE_FAULT_IN_NONPAGED_AREA", HintRam),
        [0x00000077] = ("KERNEL_STACK_INPAGE_ERROR", HintDisk),
        [0x0000007A] = ("KERNEL_DATA_INPAGE_ERROR", HintDisk),
        [0x0000007E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", HintDriver),
        [0x0000009F] = ("DRIVER_POWER_STATE_FAILURE", HintDriver),
        [0x000000C2] = ("BAD_POOL_CALLER", HintDriver),
        [0x000000D1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", HintDriver),
        [0x000000EF] = ("CRITICAL_PROCESS_DIED", HintSystem),
        [0x000000F4] = ("CRITICAL_OBJECT_TERMINATION", HintDisk),
        [0x00000101] = ("CLOCK_WATCHDOG_TIMEOUT", HintHardware),
        [0x00000116] = ("VIDEO_TDR_FAILURE", HintGpu),
        [0x00000117] = ("VIDEO_TDR_TIMEOUT_DETECTED", HintGpu),
        [0x00000119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR", HintGpu),
        [0x00000124] = ("WHEA_UNCORRECTABLE_ERROR", HintHardware),
        [0x00000133] = ("DPC_WATCHDOG_VIOLATION", HintDriver),
        [0x00000139] = ("KERNEL_SECURITY_CHECK_FAILURE", HintDriver),
        [0x0000013A] = ("KERNEL_MODE_HEAP_CORRUPTION", HintDriver),
        [0x00000154] = ("UNEXPECTED_STORE_EXCEPTION", HintDisk),
        [0x000001A5] = ("ACPI_BIOS_ERROR", HintHardware),
        [0x0000007B] = ("INACCESSIBLE_BOOT_DEVICE", HintDisk),
        [0x000000ED] = ("UNMOUNTABLE_BOOT_VOLUME", HintDisk),
        [0x0000012B] = ("FAULTY_HARDWARE_CORRUPTED_PAGE", HintRam),
        [0x00000109] = ("CRITICAL_STRUCTURE_CORRUPTION", HintRam),
        [0x000000BE] = ("ATTEMPTED_WRITE_TO_READONLY_MEMORY", HintDriver),
        [0x000000FC] = ("ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", HintDriver),
        [0x00000024] = ("NTFS_FILE_SYSTEM", HintDisk),
        [0x0000009C] = ("MACHINE_CHECK_EXCEPTION", HintHardware),
        [0x000000E2] = ("MANUALLY_INITIATED_CRASH", HintSystem),
    };

    /// <summary>"0x0000009F" -> ("DRIVER_POWER_STATE_FAILURE", "driver").</summary>
    public static (string Name, string Hint)? Lookup(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var text = code.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? code[2..] : code;
        if (!uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value)) return null;

        // Một số mã có biến thể "0x1000007E" (bit 0x10000000) -> quy về mã gốc.
        if (Known.TryGetValue(value, out var hit) || Known.TryGetValue(value & 0x0FFFFFFF, out hit)) return hit;
        return null;
    }
}

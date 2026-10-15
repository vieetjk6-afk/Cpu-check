using PcCheck.Core.Localization;

namespace PcCheck.Core.Models;

public enum Severity
{
    Ok = 0,
    Info = 1,
    Warning = 2,
    Critical = 3,
}

public enum IssueCategory
{
    Hardware,
    Temperature,
    Storage,
    Compatibility,
    Driver,
    Junk,
    Software,
    System,
    Security,
}

/// <summary>Loại hành động khắc phục. Analyzer chỉ tạo dữ liệu; việc thực thi do FixService / UI đảm nhận.</summary>
public enum FixKind
{
    /// <summary>Chuyển tới một trang trong ứng dụng (Arg = khóa trang).</summary>
    Navigate,

    /// <summary>Mở URI: ms-settings:, https:, windowsdefender: ... (Arg = URI).</summary>
    OpenUri,

    /// <summary>Chạy công cụ hệ thống (Arg = khóa công cụ, xem <see cref="Fixes.SystemTools"/>).</summary>
    RunTool,

    /// <summary>Khởi động lại máy.</summary>
    Restart,
}

public sealed record FixAction(FixKind Kind, string? Arg, LocText Label, bool RequiresConfirmation = false)
{
    public LocText? ConfirmMessage { get; init; }
}

public sealed class Issue
{
    public required string Id { get; init; }
    public required IssueCategory Category { get; init; }
    public required Severity Severity { get; init; }
    public required LocText Title { get; init; }
    public required LocText Detail { get; init; }
    public LocText? Recommendation { get; init; }
    public IReadOnlyList<FixAction> Fixes { get; init; } = [];

    /// <summary>Hướng dẫn chi tiết (nguyên nhân + các bước xử lý), gắn theo loại vấn đề.</summary>
    public IssueGuide? Guide { get; set; }
}

/// <summary>Nguyên nhân thường gặp và các bước khắc phục (mỗi bước một dòng trong chuỗi Steps).</summary>
public sealed record IssueGuide(string Key, LocText Cause, LocText Steps);

public enum CheckStatus
{
    Pass,
    Info,
    Warning,
    Fail,
    Unknown,
}

public sealed class CompatibilityCheck
{
    public required string Id { get; init; }
    public required LocText Group { get; init; }
    public required LocText Title { get; init; }
    public required CheckStatus Status { get; init; }
    public required LocText Detail { get; init; }
    public LocText? Recommendation { get; init; }
    public IReadOnlyList<FixAction> Fixes { get; init; } = [];
}

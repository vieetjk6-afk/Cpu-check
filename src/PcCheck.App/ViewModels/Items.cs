using PcCheck.App.Infrastructure;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;

namespace PcCheck.App.ViewModels;

/// <summary>Nút khắc phục hiển thị trên giao diện.</summary>
public sealed class FixItem(FixAction action)
{
    public FixAction Action { get; } = action;

    public string Label { get; } = action.Label.ToString();
}

/// <summary>Một bước trong hướng dẫn khắc phục (đánh số).</summary>
public sealed record StepItem(int Number, string Text);

/// <summary>Một vấn đề đã dịch sẵn sang ngôn ngữ hiện tại. Bấm vào thẻ để xem nguyên nhân và các bước khắc phục.</summary>
public sealed class IssueItem : ObservableObject
{
    // Giữ trạng thái mở rộng khi danh sách được dựng lại (đổi ngôn ngữ, quét lại, đổi bộ lọc).
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
    private bool _isExpanded;

    public IssueItem(Issue issue)
    {
        Issue = issue;
        Severity = issue.Severity;
        Title = issue.Title.ToString();
        Detail = issue.Detail.ToString();
        Recommendation = issue.Recommendation?.ToString() ?? "";
        Category = Labels.Category(issue.Category);
        SeverityText = Labels.Severity(issue.Severity);
        Fixes = issue.Fixes.Select(f => new FixItem(f)).ToList();
        if (issue.Guide is { } guide)
        {
            Cause = guide.Cause.ToString();
            Steps = guide.Steps.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((text, i) => new StepItem(i + 1, text))
                .ToList();
        }

        _isExpanded = HasGuide && Expanded.Contains(issue.Id);
    }

    public Issue Issue { get; }
    public Severity Severity { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Recommendation { get; }
    public string Category { get; }
    public string SeverityText { get; }
    public IReadOnlyList<FixItem> Fixes { get; }
    public string Cause { get; } = "";
    public IReadOnlyList<StepItem> Steps { get; } = [];
    public bool HasGuide => Steps.Count > 0 || Cause.Length > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value && HasGuide)) return;
            if (_isExpanded) Expanded.Add(Issue.Id);
            else Expanded.Remove(Issue.Id);
        }
    }
}

/// <summary>Cặp nhãn - giá trị trong thẻ thông tin.</summary>
public sealed record InfoItem(string Label, string Value);

/// <summary>Nhóm thông tin con (vd 1 thanh RAM, 1 ổ cứng).</summary>
public sealed class InfoGroup(string title, string subtitle, IReadOnlyList<InfoItem> items, Severity? status = null, string statusText = "")
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public IReadOnlyList<InfoItem> Items { get; } = items;
    public Severity? Status { get; } = status;
    public string StatusText { get; } = statusText;
}

/// <summary>Thẻ thông tin của một linh kiện (bấm tiêu đề để thu gọn / mở rộng).</summary>
public sealed class InfoSection(string glyph, string titleKey, string subtitle, IReadOnlyList<InfoItem> items, IReadOnlyList<InfoGroup>? groups = null)
    : ObservableObject
{
    private bool _isExpanded = true;

    public string Glyph { get; } = glyph;
    public string Title { get; } = Loc.T(titleKey);
    public string Subtitle { get; } = subtitle;
    public IReadOnlyList<InfoItem> Items { get; } = items;
    public IReadOnlyList<InfoGroup> Groups { get; } = groups ?? [];

    /// <summary>Khóa ổn định (khóa ngôn ngữ của tiêu đề) để nhớ trạng thái thu gọn.</summary>
    public string Key { get; } = titleKey;

    /// <summary>Tóm tắt số mục khi đang thu gọn.</summary>
    public int Count => Items.Count + Groups.Count;

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
}

/// <summary>Ô số liệu nhỏ có màu trạng thái.</summary>
public sealed class StatTile : ObservableObject
{
    private string _value = "—";
    private string _caption = "";
    private Severity _severity = Severity.Ok;
    private double _percent;
    private IReadOnlyList<double>? _history;

    public StatTile(string glyph, string titleKey)
    {
        Glyph = glyph;
        TitleKey = titleKey;
    }

    public string Glyph { get; }
    public string TitleKey { get; }
    public string Title => Loc.T(TitleKey);

    public string Value { get => _value; set => Set(ref _value, value); }
    public string Caption { get => _caption; set => Set(ref _caption, value); }
    public Severity Severity { get => _severity; set => Set(ref _severity, value); }
    public double Percent { get => _percent; set => Set(ref _percent, value); }
    public IReadOnlyList<double>? History { get => _history; set => Set(ref _history, value); }

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

/// <summary>Bộ đệm vòng cho lịch sử số liệu (biểu đồ mini).</summary>
public sealed class History(int capacity = 60)
{
    private readonly Queue<double> _values = new();

    public IReadOnlyList<double> Push(double value)
    {
        _values.Enqueue(value);
        while (_values.Count > capacity) _values.Dequeue();
        return _values.ToArray();
    }
}

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

/// <summary>Một vấn đề đã dịch sẵn sang ngôn ngữ hiện tại.</summary>
public sealed class IssueItem
{
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
    }

    public Issue Issue { get; }
    public Severity Severity { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Recommendation { get; }
    public string Category { get; }
    public string SeverityText { get; }
    public IReadOnlyList<FixItem> Fixes { get; }
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

/// <summary>Thẻ thông tin của một linh kiện.</summary>
public sealed class InfoSection(string glyph, string title, string subtitle, IReadOnlyList<InfoItem> items, IReadOnlyList<InfoGroup>? groups = null)
{
    public string Glyph { get; } = glyph;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public IReadOnlyList<InfoItem> Items { get; } = items;
    public IReadOnlyList<InfoGroup> Groups { get; } = groups ?? [];
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

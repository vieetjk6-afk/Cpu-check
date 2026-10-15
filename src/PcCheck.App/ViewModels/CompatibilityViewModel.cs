using PcCheck.Core.Localization;
using PcCheck.Core.Models;

namespace PcCheck.App.ViewModels;

public sealed class CheckItem(CompatibilityCheck check)
{
    public CheckStatus Status { get; } = check.Status;
    public string StatusText { get; } = Labels.Check(check.Status);
    public string Title { get; } = check.Title.ToString();
    public string Detail { get; } = check.Detail.ToString();
    public string Recommendation { get; } = check.Recommendation?.ToString() ?? "";
    public IReadOnlyList<FixItem> Fixes { get; } = check.Fixes.Select(f => new FixItem(f)).ToList();
}

public sealed class CheckGroup(string title, IReadOnlyList<CheckItem> items)
{
    public string Title { get; } = title;
    public IReadOnlyList<CheckItem> Items { get; } = items;
}

/// <summary>Trang kiểm tra tương thích phần cứng / Windows 11.</summary>
public sealed class CompatibilityViewModel(MainViewModel main) : PageViewModel(main)
{
    private IReadOnlyList<CheckGroup> _groups = [];
    private int _pass;
    private int _warn;
    private int _fail;

    public IReadOnlyList<CheckGroup> Groups { get => _groups; private set => Set(ref _groups, value); }
    public int PassCount { get => _pass; private set => Set(ref _pass, value); }
    public int WarnCount { get => _warn; private set => Set(ref _warn, value); }
    public int FailCount { get => _fail; private set => Set(ref _fail, value); }

    protected override void Rebuild()
    {
        var checks = State.Result?.Checks ?? [];
        Groups = checks
            .GroupBy(c => c.Group.Key)
            .Select(g => new CheckGroup(g.First().Group.ToString(), g.Select(c => new CheckItem(c)).ToList()))
            .ToList();
        PassCount = checks.Count(c => c.Status == CheckStatus.Pass);
        WarnCount = checks.Count(c => c.Status is CheckStatus.Warning or CheckStatus.Info);
        FailCount = checks.Count(c => c.Status == CheckStatus.Fail);
    }
}

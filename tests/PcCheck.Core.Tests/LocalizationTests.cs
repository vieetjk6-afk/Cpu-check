using System.Globalization;
using System.Text.RegularExpressions;
using PcCheck.Core.Analysis;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;

namespace PcCheck.Core.Tests;

public sealed partial class LocalizationTests
{
    private static readonly Dictionary<string, string> Vi = Loc.Load(Loc.Vietnamese);
    private static readonly Dictionary<string, string> En = Loc.Load(Loc.English);

    [Fact]
    public void BothLanguagesHaveTheSameKeys()
    {
        Assert.NotEmpty(Vi);
        Assert.Empty(Vi.Keys.Except(En.Keys));
        Assert.Empty(En.Keys.Except(Vi.Keys));
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        foreach (var (key, vi) in Vi)
        {
            Assert.Equal(Placeholders(vi), Placeholders(En[key]));
        }
    }

    [Fact]
    public void EveryTemplateFormatsWithoutError()
    {
        var args = new object?[] { 1, 2, 3, 4, 5 };
        foreach (var dict in new[] { Vi, En })
        {
            foreach (var (key, template) in dict)
            {
                var ex = Record.Exception(() => string.Format(CultureInfo.InvariantCulture, template, args));
                Assert.True(ex is null, $"Template '{key}' lỗi định dạng: {ex?.Message}");
            }
        }
    }

    [Fact]
    public void EveryKeyUsedInSourceExists()
    {
        var root = FindRepoRoot();
        var missing = new SortedSet<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(file);
            IEnumerable<string> keys = file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                ? XamlKey().Matches(text).Select(m => m.Groups[1].Value)
                : file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    ? CsKey().Matches(text).Select(m => m.Groups[1].Value).Where(k => !FileLike().IsMatch(k))
                    : [];
            foreach (var key in keys.Where(k => !Vi.ContainsKey(k))) missing.Add($"{key} ({Path.GetFileName(file)})");
        }

        Assert.True(missing.Count == 0, "Thiếu khóa dịch: " + string.Join(", ", missing));
    }

    [Fact]
    public void DynamicKeysExist()
    {
        foreach (var tool in SystemTools.QuickTools)
        {
            Assert.True(Vi.ContainsKey($"tool.{tool.Key}.name"), tool.Key);
            Assert.True(Vi.ContainsKey($"tool.{tool.Key}.desc"), tool.Key);
            if (tool.RequiresConfirmation) Assert.True(Vi.ContainsKey($"tool.{tool.Key}.confirm"), tool.Key);
        }

        foreach (var code in DeviceErrorCodes.KnownCodes) Assert.True(Vi.ContainsKey($"devcode.{code}"), code.ToString(CultureInfo.InvariantCulture));
        foreach (var hint in BugChecks.AllHints) Assert.True(Vi.ContainsKey($"bugcheck.hint.{hint}"), hint);
        foreach (var metric in Util.WidgetSettings.AllMetrics)
        {
            Assert.True(Vi.ContainsKey($"widget.metric.{metric}"), metric);
            Assert.True(Vi.ContainsKey($"widget.metric_desc.{metric}"), metric);
        }
    }

    [Fact]
    public void SwitchingLanguageChangesText()
    {
        Loc.Instance.SetLanguage(Loc.English);
        Assert.Equal("Overview", Loc.T("nav.dashboard"));
        var text = new LocText("issue.bsod.title", 2, 30);
        Assert.Equal("2 blue screens in 30 days", text.ToString());

        Loc.Instance.SetLanguage(Loc.Vietnamese);
        Assert.Equal("Tổng quan", Loc.T("nav.dashboard"));
        Assert.Equal("Màn hình xanh 2 lần trong 30 ngày", text.ToString());
    }

    [Fact]
    public void UnknownKeyFallsBackToKey()
    {
        Assert.Equal("no.such.key", Loc.T("no.such.key"));
    }

    private static string[] Placeholders(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PcCheck.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục gốc repo");
    }

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{l:T ([^}\s]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex("\"([a-z][a-z0-9_-]*(?:\\.[a-z0-9_-]+)+)\"")]
    private static partial Regex CsKey();

    [GeneratedRegex(@"\.(exe|msc|cpl|dll|json|log|ini|xaml|html)$")]
    private static partial Regex FileLike();
}

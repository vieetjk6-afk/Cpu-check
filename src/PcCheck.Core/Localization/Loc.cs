using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PcCheck.Core.Localization;

/// <summary>
/// Bộ chuỗi song ngữ (vi/en). Chuỗi nằm trong Localization/Lang/*.json (nhúng vào assembly).
/// UI bind vào indexer <c>Loc.Instance[key]</c>; khi đổi ngôn ngữ, sự kiện "Item[]" được phát để cập nhật toàn bộ.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Vietnamese = "vi";
    public const string English = "en";
    public static readonly IReadOnlyList<string> SupportedLanguages = [Vietnamese, English];

    public static Loc Instance { get; } = new();

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _current;
    private readonly Dictionary<string, string> _fallback;

    private Loc()
    {
        _fallback = Load(English);
        _current = Load(Vietnamese);
        Culture = CreateCulture(Vietnamese);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Phát ra sau khi đổi ngôn ngữ, để các ViewModel dựng lại nội dung động.</summary>
    public event EventHandler? LanguageChanged;

    public string Language { get; private set; } = Vietnamese;

    public CultureInfo Culture { get; private set; }

    public string this[string key] =>
        _current.TryGetValue(key, out var value) ? value
        : _fallback.TryGetValue(key, out value) ? value
        : key;

    public bool Contains(string key) => _current.ContainsKey(key) || _fallback.ContainsKey(key);

    public void SetLanguage(string language)
    {
        language = SupportedLanguages.Contains(language) ? language : English;
        if (language == Language) return;

        _current = Load(language);
        Language = language;
        Culture = CreateCulture(language);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args)
    {
        var template = Instance[key];
        if (args.Length == 0) return template;
        try
        {
            return string.Format(Instance.Culture, template, args.Select(ResolveArg).ToArray());
        }
        catch (FormatException)
        {
            return template;
        }
    }

    internal static object? ResolveArg(object? arg) => arg switch
    {
        LocText text => text.ToString(),
        _ => arg,
    };

    public static string DefaultLanguageForSystem() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(Vietnamese, StringComparison.OrdinalIgnoreCase)
            ? Vietnamese
            : English;

    private static CultureInfo CreateCulture(string language) =>
        CultureInfo.GetCultureInfo(language == Vietnamese ? "vi-VN" : "en-US");

    internal static Dictionary<string, string> Load(string language)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(language, out var cached)) return cached;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"PcCheck.Lang.{language}.json");
            if (stream is not null)
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (parsed is not null) dict = new Dictionary<string, string>(parsed, StringComparer.Ordinal);
            }

            Cache[language] = dict;
            return dict;
        }
    }
}

using System.ComponentModel;
using System.Globalization;

namespace Translator.Core;

public enum AppUILanguage
{
    System,
    Ru,
    En,
    Es,
    De,
    Fr,
    It,
    Pt,
    Zh,
    Ja,
    Ko,
    Tr,
    Uk,
}

public static class AppUILanguageInfo
{
    /// <summary>Lowercase code used as a translation-table key ("system" has none).</summary>
    public static string? Code(this AppUILanguage language) => language switch
    {
        AppUILanguage.System => null,
        _ => language.ToString().ToLowerInvariant(),
    };

    public static AppUILanguage Parse(string? value) =>
        Enum.TryParse<AppUILanguage>(value, ignoreCase: true, out var language) ? language : AppUILanguage.System;
}

/// <summary>
/// Static translation table for the 12 supported UI languages. Every user-visible string
/// goes through <see cref="T"/> / <see cref="Format"/>. Data lives in L10n.Strings.cs.
/// </summary>
public static partial class L10n
{
    private static AppUILanguage _selected = AppUILanguage.System;

    /// <summary>Raised whenever the effective UI language changes (selection or, indirectly, on Format calls it does not track system changes mid-run).</summary>
    public static event EventHandler? LanguageChanged;

    public static AppUILanguage Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }
            _selected = value;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>The effective language code in use right now (one of the 12, never "system").</summary>
    public static string CurrentCode => _selected == AppUILanguage.System ? SystemCode : _selected.Code()!;

    private static readonly HashSet<string> SupportedCodes = Enum.GetValues<AppUILanguage>()
        .Select(l => l.Code())
        .Where(c => c is not null)
        .Select(c => c!)
        .ToHashSet();

    /// <summary>Best-effort code derived from the OS UI culture and its parents ("zh*"/"pt*" normalize to "zh"/"pt").</summary>
    public static string SystemCode
    {
        get
        {
            for (var culture = CultureInfo.CurrentUICulture; culture is not null && culture != CultureInfo.InvariantCulture; culture = culture.Parent)
            {
                var code = culture.TwoLetterISOLanguageName.ToLowerInvariant();
                if (code is "zh" or "pt" || SupportedCodes.Contains(code))
                {
                    return code;
                }
            }
            return "en";
        }
    }

    /// <summary>Looks up <paramref name="key"/> for the current language, falling back to en, then ru, then the key itself.</summary>
    public static string T(string key) =>
        Lookup(Translations, key, CurrentCode) ?? key;

    /// <summary>Composite-formats <see cref="T"/> using the culture of the current UI language.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureFor(CurrentCode), T(key), args);

    /// <summary>Localized name of a language given its Google-style code ("zh-CN" normalizes to "zh").</summary>
    public static string LanguageName(string googleCode)
    {
        var normalized = string.Equals(googleCode, "zh-CN", StringComparison.OrdinalIgnoreCase) ? "zh" : googleCode;
        return Lookup(LanguageNames, normalized, CurrentCode) ?? googleCode;
    }

    /// <summary>Native display name for the UI language picker ("System" is localized).</summary>
    public static string DisplayName(AppUILanguage language) =>
        language == AppUILanguage.System ? T("language.system") : NativeNames.GetValueOrDefault(language, language.ToString());

    public static IReadOnlyCollection<string> Keys => Translations.Keys;

    /// <summary>Test-only accessor: full translation table (key -> language code -> text).</summary>
    internal static IReadOnlyDictionary<string, Dictionary<string, string>> TranslationsForTests => Translations;

    /// <summary>Test-only accessor: full language-name table.</summary>
    internal static IReadOnlyDictionary<string, Dictionary<string, string>> LanguageNamesForTests => LanguageNames;

    internal static CultureInfo CultureFor(string code)
    {
        var name = code switch
        {
            "ru" => "ru-RU",
            "en" => "en-US",
            "es" => "es-ES",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "it" => "it-IT",
            "pt" => "pt-BR",
            "zh" => "zh-CN",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "tr" => "tr-TR",
            "uk" => "uk-UA",
            _ => null,
        };
        if (name is null)
        {
            return CultureInfo.InvariantCulture;
        }
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static string? Lookup(IReadOnlyDictionary<string, Dictionary<string, string>> table, string key, string currentCode)
    {
        if (!table.TryGetValue(key, out var byLanguage))
        {
            return null;
        }
        if (byLanguage.TryGetValue(currentCode, out var text))
        {
            return text;
        }
        if (byLanguage.TryGetValue("en", out text))
        {
            return text;
        }
        return byLanguage.TryGetValue("ru", out text) ? text : null;
    }
}

/// <summary>
/// XAML-friendly localization source: <c>{Binding [app.title], Source={x:Static core:LocalizationSource.Instance}}</c>.
/// Raises the WPF "any indexer value may have changed" notification when the UI language changes.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    public static readonly LocalizationSource Instance = new();

    private LocalizationSource() => L10n.LanguageChanged += (_, _) => OnLanguageChanged();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => L10n.T(key);

    private void OnLanguageChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

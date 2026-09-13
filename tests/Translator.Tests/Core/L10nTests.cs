using System.Text.RegularExpressions;
using Translator.Core;

namespace Translator.Tests.Core;

public class L10nTests
{
    private static readonly string[] Languages = ["ru", "en", "es", "de", "fr", "it", "pt", "zh", "ja", "ko", "tr", "uk"];

    public L10nTests() => L10n.Selected = AppUILanguage.En;

    [Fact]
    public void EveryKeyHasAllTwelveLanguages()
    {
        foreach (var (key, byLanguage) in L10n.TranslationsForTests)
        {
            foreach (var language in Languages)
            {
                Assert.True(byLanguage.ContainsKey(language), $"Key '{key}' is missing language '{language}'");
                Assert.False(string.IsNullOrWhiteSpace(byLanguage[language]), $"Key '{key}' has empty text for '{language}'");
            }
        }
    }

    [Fact]
    public void EveryKeyHasSamePlaceholdersAsEnglish()
    {
        foreach (var (key, byLanguage) in L10n.TranslationsForTests)
        {
            var englishPlaceholders = ExtractPlaceholders(byLanguage["en"]);
            foreach (var language in Languages)
            {
                var placeholders = ExtractPlaceholders(byLanguage[language]);
                Assert.True(
                    englishPlaceholders.SetEquals(placeholders),
                    $"Key '{key}' language '{language}' has placeholders [{string.Join(",", placeholders)}] " +
                    $"but English has [{string.Join(",", englishPlaceholders)}]");
            }
        }
    }

    [Fact]
    public void LanguageNamesCoverAllTwelveLanguagesForEachCode()
    {
        foreach (var (code, byLanguage) in L10n.LanguageNamesForTests)
        {
            foreach (var language in Languages)
            {
                Assert.True(byLanguage.ContainsKey(language), $"Language name '{code}' missing '{language}'");
            }
        }
        Assert.Equal(12, L10n.LanguageNamesForTests.Count);
    }

    [Fact]
    public void TReturnsCurrentLanguageText()
    {
        L10n.Selected = AppUILanguage.Ru;
        Assert.Equal("Настройки", L10n.T("settings"));
        L10n.Selected = AppUILanguage.En;
        Assert.Equal("Settings", L10n.T("settings"));
    }

    [Fact]
    public void TFallsBackToKeyWhenMissing()
    {
        Assert.Equal("no.such.key", L10n.T("no.such.key"));
    }

    [Fact]
    public void FormatSubstitutesArguments()
    {
        L10n.Selected = AppUILanguage.En;
        Assert.Equal("Version 2.0 is available.", L10n.Format("update.available", "2.0"));
    }

    [Fact]
    public void LanguageNameNormalizesZhCn()
    {
        L10n.Selected = AppUILanguage.En;
        Assert.Equal(L10n.LanguageName("zh"), L10n.LanguageName("zh-CN"));
        Assert.Equal("Chinese", L10n.LanguageName("zh-CN"));
    }

    [Fact]
    public void DisplayNameForSystemUsesLocalizedLabel()
    {
        L10n.Selected = AppUILanguage.En;
        Assert.Equal(L10n.T("language.system"), L10n.DisplayName(AppUILanguage.System));
    }

    [Fact]
    public void DisplayNameForLanguagesIsNative()
    {
        Assert.Equal("Русский", L10n.DisplayName(AppUILanguage.Ru));
        Assert.Equal("Deutsch", L10n.DisplayName(AppUILanguage.De));
    }

    [Fact]
    public void SelectedChangeRaisesLanguageChanged()
    {
        var raised = false;
        L10n.Selected = AppUILanguage.En;
        EventHandler handler = (_, _) => raised = true;
        L10n.LanguageChanged += handler;
        try
        {
            L10n.Selected = AppUILanguage.Ru;
            Assert.True(raised);
        }
        finally
        {
            L10n.LanguageChanged -= handler;
            L10n.Selected = AppUILanguage.En;
        }
    }

    private static HashSet<string> ExtractPlaceholders(string text) =>
        Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).ToHashSet();
}

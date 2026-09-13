using Translator.Core;
using Translator.UI;
using Translator.UI.Windows;

namespace Translator.Tests.UI;

public class PrivacyCapsuleAndTextTests : IDisposable
{
    public PrivacyCapsuleAndTextTests() => L10n.Selected = AppUILanguage.En;

    public void Dispose() => L10n.Selected = AppUILanguage.System;

    [Fact]
    public void Pending_capsule_shows_only_an_icon()
    {
        Assert.Equal(new PrivacyCapsuleContent(Icons.Shield, null), PrivacyCapsule.Describe(EngineKind.None, offlineOnly: false));
    }

    [Fact]
    public void Google_capsule_shows_the_globe_and_the_engine_name()
    {
        Assert.Equal(new PrivacyCapsuleContent(Icons.Globe, "Google"), PrivacyCapsule.Describe(EngineKind.Google, offlineOnly: false));
    }

    [Theory]
    [InlineData(EngineKind.Offline, false)]
    [InlineData(EngineKind.None, true)]
    public void Offline_and_offline_only_show_the_lock(EngineKind engine, bool offlineOnly)
    {
        Assert.Equal(new PrivacyCapsuleContent(Icons.Lock, "Offline"), PrivacyCapsule.Describe(engine, offlineOnly));
    }

    [Theory]
    [InlineData(AppUILanguage.Ru)]
    [InlineData(AppUILanguage.En)]
    [InlineData(AppUILanguage.Es)]
    [InlineData(AppUILanguage.De)]
    [InlineData(AppUILanguage.Fr)]
    [InlineData(AppUILanguage.It)]
    [InlineData(AppUILanguage.Pt)]
    [InlineData(AppUILanguage.Zh)]
    [InlineData(AppUILanguage.Ja)]
    [InlineData(AppUILanguage.Ko)]
    [InlineData(AppUILanguage.Tr)]
    [InlineData(AppUILanguage.Uk)]
    public void Capsule_labels_are_short_in_every_language(AppUILanguage language)
    {
        L10n.Selected = language;

        foreach (var key in new[] { "engine.google.label", "engine.offline.label" })
        {
            var name = PrivacyCapsule.EngineName(key);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.DoesNotContain('·', name);
            Assert.DoesNotContain('・', name);
            Assert.True(name.Length < L10n.T(key).Length);
        }
        Assert.False(TextFormat.TrimTrailingEllipsis(L10n.T("offline.languages")).EndsWith('…'));
    }

    [Theory]
    [InlineData("Offline languages…", "Offline languages")]
    [InlineData("Open...", "Open")]
    [InlineData("离线语言…", "离线语言")]
    [InlineData("Translation history", "Translation history")]
    public void TrimTrailingEllipsis(string text, string expected)
    {
        Assert.Equal(expected, TextFormat.TrimTrailingEllipsis(text));
    }

    [Theory]
    [InlineData("Couldn't download 'uk': network error (No such host is known.).", "network error (No such host is known.)")]
    [InlineData("Language 'uk' is already downloading.", "Language 'uk' is already downloading")]
    public void Download_failures_drop_the_engine_prefix(string message, string expected)
    {
        Assert.Equal(expected, OfflineLanguagesWindow.DescribeFailure(message));
    }
}

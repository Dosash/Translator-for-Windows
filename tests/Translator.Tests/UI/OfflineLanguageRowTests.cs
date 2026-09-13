using Translator.Core;
using Translator.Offline;
using Translator.UI.Windows;

namespace Translator.Tests.UI;

public class OfflineLanguageRowTests : IDisposable
{
    public OfflineLanguageRowTests() => L10n.Selected = AppUILanguage.En;

    public void Dispose() => L10n.Selected = AppUILanguage.System;

    [Fact]
    public void Not_installed_offers_download()
    {
        var actions = OfflineRowActions.For(OfflineLanguageState.NotInstalled, downloading: false, updateFlagged: false);

        Assert.Equal(new OfflineRowActions(true, false, false, false, false, false, OfflineDot.Missing), actions);
    }

    [Fact]
    public void Installed_shows_downloaded_and_remove()
    {
        var actions = OfflineRowActions.For(OfflineLanguageState.Installed, downloading: false, updateFlagged: false);

        Assert.Equal(new OfflineRowActions(false, false, true, true, false, false, OfflineDot.Ready), actions);
    }

    [Theory]
    [InlineData(OfflineLanguageState.UpdateAvailable, false)]
    [InlineData(OfflineLanguageState.Installed, true)]
    public void Updates_offer_update_and_remove(OfflineLanguageState state, bool flagged)
    {
        var actions = OfflineRowActions.For(state, downloading: false, updateFlagged: flagged);

        Assert.True(actions.ShowUpdate);
        Assert.True(actions.ShowRemove);
        Assert.False(actions.ShowDownload);
        Assert.Equal(OfflineDot.Ready, actions.Dot);
    }

    [Fact]
    public void Flagged_but_missing_language_offers_update_only()
    {
        var actions = OfflineRowActions.For(OfflineLanguageState.NotInstalled, downloading: false, updateFlagged: true);

        Assert.True(actions.ShowUpdate);
        Assert.False(actions.ShowRemove);
    }

    [Theory]
    [InlineData(OfflineLanguageState.NotInstalled, true)]
    [InlineData(OfflineLanguageState.Downloading, false)]
    public void Downloading_shows_progress_only(OfflineLanguageState state, bool downloading)
    {
        var actions = OfflineRowActions.For(state, downloading, updateFlagged: false);

        Assert.Equal(new OfflineRowActions(false, false, false, false, false, true, OfflineDot.Missing), actions);
    }

    [Fact]
    public void Unsupported_shows_a_dimmed_label()
    {
        var actions = OfflineRowActions.For(OfflineLanguageState.Unsupported, downloading: false, updateFlagged: false);

        Assert.True(actions.ShowUnsupported);
        Assert.Equal(OfflineDot.Unavailable, actions.Dot);
    }

    [Fact]
    public void Row_texts_use_localized_formats()
    {
        var row = new OfflineLanguageRow("de") { SizeBytes = 221_400_000, Progress = 41.6 };

        Assert.Equal("German", row.Name);
        Assert.Equal("≈221 MB", row.SizeText);
        Assert.Equal("Downloading… 42%", row.ProgressText);
    }

    [Fact]
    public void Unknown_size_is_hidden()
    {
        Assert.Equal(string.Empty, new OfflineLanguageRow("de").SizeText);
    }

    [Fact]
    public void Changing_state_notifies_actions()
    {
        var row = new OfflineLanguageRow("fr");
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.State = OfflineLanguageState.Installed;

        Assert.Contains(nameof(OfflineLanguageRow.Actions), changed);
        Assert.True(row.Actions.ShowRemove);
    }
}

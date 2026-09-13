using System.IO;
using System.Windows.Input;
using Translator.Core;

namespace Translator.Tests.Core;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "TranslatorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort cleanup */ }
    }

    [Fact]
    public void DefaultsWhenNoFileExists()
    {
        var settings = new SettingsStore(_path);

        Assert.Equal(Hotkey.DefaultSelection, settings.SelectionHotkey);
        Assert.Equal(Hotkey.DefaultClipboard, settings.ClipboardHotkey);
        Assert.Equal(Hotkey.DefaultPanel, settings.PanelHotkey);
        Assert.True(settings.AutoTranslate);
        Assert.False(settings.OfflineOnly);
        Assert.Equal(AppTheme.CalmGlass, settings.AppTheme);
        Assert.Equal(PanelSizeMode.Standard, settings.PanelSizeMode);
        Assert.Equal(AppUILanguage.System, settings.AppLanguage);
        Assert.False(settings.HasCompletedFirstRun);
        Assert.Equal("auto", settings.SourceCode);
        Assert.NotEqual("en", settings.TargetCode);
    }

    [Fact]
    public void PersistsAndReloadsChangedValues()
    {
        var settings = new SettingsStore(_path);
        settings.AutoTranslate = false;
        settings.OfflineOnly = true;
        settings.AppTheme = AppTheme.NeonGlass;
        settings.PanelSizeMode = PanelSizeMode.Wide;
        settings.SourceCode = "en";
        settings.TargetCode = "de";
        settings.HasCompletedFirstRun = true;
        settings.SelectionHotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.G);

        var reloaded = new SettingsStore(_path);
        Assert.False(reloaded.AutoTranslate);
        Assert.True(reloaded.OfflineOnly);
        Assert.Equal(AppTheme.NeonGlass, reloaded.AppTheme);
        Assert.Equal(PanelSizeMode.Wide, reloaded.PanelSizeMode);
        Assert.Equal("en", reloaded.SourceCode);
        Assert.Equal("de", reloaded.TargetCode);
        Assert.True(reloaded.HasCompletedFirstRun);
        Assert.Equal(new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.G), reloaded.SelectionHotkey);
    }

    [Fact]
    public void DisabledHotkeyRoundTripsAsNone()
    {
        var settings = new SettingsStore(_path);
        settings.ClipboardHotkey = Hotkey.None;

        var reloaded = new SettingsStore(_path);
        Assert.Equal(Hotkey.None, reloaded.ClipboardHotkey);
        Assert.True(reloaded.ClipboardHotkey.IsNone);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaultsWithoutThrowing()
    {
        File.WriteAllText(_path, "not { valid json");
        var settings = new SettingsStore(_path);

        Assert.Equal(Hotkey.DefaultSelection, settings.SelectionHotkey);
        Assert.True(settings.AutoTranslate);
    }

    [Fact]
    public void ChangingHotkeyRaisesHotkeysChanged()
    {
        var settings = new SettingsStore(_path);
        var raised = 0;
        settings.HotkeysChanged += (_, _) => raised++;

        settings.SelectionHotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Z);
        settings.PanelHotkey = Hotkey.None;

        Assert.Equal(2, raised);
    }

    [Fact]
    public void AppLanguageSetterUpdatesL10nSelected()
    {
        var settings = new SettingsStore(_path);
        try
        {
            settings.AppLanguage = AppUILanguage.Fr;
            Assert.Equal(AppUILanguage.Fr, L10n.Selected);
        }
        finally
        {
            L10n.Selected = AppUILanguage.System;
        }
    }

    [Fact]
    public void CompleteFirstRunPersists()
    {
        var settings = new SettingsStore(_path);
        settings.CompleteFirstRun();
        Assert.True(settings.HasCompletedFirstRun);

        var reloaded = new SettingsStore(_path);
        Assert.True(reloaded.HasCompletedFirstRun);
    }

    [Fact]
    public void RecordingStateChangeRaisesEvent()
    {
        var settings = new SettingsStore(_path);
        bool? lastValue = null;
        settings.RecordingStateChanged += (_, value) => lastValue = value;

        settings.IsRecordingHotkey = true;
        Assert.True(lastValue);

        settings.IsRecordingHotkey = false;
        Assert.False(lastValue);
    }

    [Fact]
    public void LaunchAtLoginSetterUpdatesValueViaAutostartStub()
    {
        var enabled = false;
        var settings = new SettingsStore(_path, () => enabled, value => enabled = value);
        settings.LaunchAtLogin = true;
        Assert.True(settings.LaunchAtLogin);
        Assert.True(enabled);
        Assert.Null(settings.LoginItemMessage);
    }

    [Fact]
    public void LaunchAtLoginFailureRevertsAndReportsMessage()
    {
        var settings = new SettingsStore(_path, () => false, _ => throw new UnauthorizedAccessException("denied"));
        settings.LaunchAtLogin = true;
        Assert.False(settings.LaunchAtLogin);
        Assert.NotNull(settings.LoginItemMessage);
    }
}

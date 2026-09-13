using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Translator.Core;

namespace Translator.UI.Windows;

/// <summary>Settings (port of macOS <c>SettingsView</c>).</summary>
public partial class SettingsWindow : AuxWindow
{
    private readonly SettingsStore _settings;
    private readonly List<OptionItem> _themes;
    private readonly List<OptionItem> _sizes;

    public SettingsWindow(SettingsStore settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = settings;

        _themes = Enum.GetValues<AppTheme>()
            .Select(theme => new OptionItem(theme, theme.CaptionKey()) { FixedName = theme.DisplayName() })
            .ToList();
        _sizes = Enum.GetValues<PanelSizeMode>()
            .Select(mode => new OptionItem(mode, mode.CaptionKey()) { NameKey = mode.NameKey() })
            .ToList();
        ThemeCards.ItemsSource = _themes;
        SizeCards.ItemsSource = _sizes;

        UpdateSwatches();
        UpdateSelection();
        UpdateLanguageOptions();
        UpdateVersionText();

        settings.PropertyChanged += OnSettingsPropertyChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            settings.PropertyChanged -= OnSettingsPropertyChanged;
            ThemeManager.ThemeChanged -= OnThemeChanged;
        };
    }

    protected override void OnLanguageChanged()
    {
        UpdateLanguageOptions();
        foreach (var item in _themes.Concat(_sizes))
        {
            item.RefreshTexts();
        }
        UpdateVersionText();
    }

    private void UpdateLanguageOptions() =>
        UiLanguagePicker.ItemsSource = Enum.GetValues<AppUILanguage>()
            .Select(language => new LanguageOption(language.ToString(), L10n.DisplayName(language)))
            .ToList();

    private void UpdateVersionText() => VersionText.Text = L10n.Format("version.footer", AppInfo.Version);

    private void UpdateSwatches()
    {
        foreach (var item in _themes)
        {
            var theme = (AppTheme)item.Value;
            item.Swatch = ThemeManager.CreateGradient(ThemePalette.PreviewColors(theme));
            var accent = new SolidColorBrush(ThemePalette.PreviewAccent(theme, ThemeManager.SystemAccent));
            accent.Freeze();
            item.Accent = accent;
        }
    }

    private void UpdateSelection()
    {
        foreach (var item in _themes)
        {
            item.IsSelected = (AppTheme)item.Value == _settings.AppTheme;
        }
        foreach (var item in _sizes)
        {
            item.IsSelected = (PanelSizeMode)item.Value == _settings.PanelSizeMode;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsStore.AppTheme) or nameof(SettingsStore.PanelSizeMode))
        {
            UpdateSelection();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateSwatches();

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OptionItem { Value: AppTheme theme })
        {
            _settings.AppTheme = theme;
        }
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OptionItem { Value: PanelSizeMode mode })
        {
            _settings.PanelSizeMode = mode;
        }
    }

    private void OnRecordingChanged(object? sender, bool isRecording) => _settings.IsRecordingHotkey = isRecording;

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _settings.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SettingsWindow: update check failed ({ex.GetType().Name})");
        }
    }

    private void OnDownloadUpdateClick(object sender, RoutedEventArgs e)
    {
        // Only ever hand an https URL from the release feed to the shell.
        if (!Uri.TryCreate(_settings.UpdateUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SettingsWindow: opening the download page failed ({ex.GetType().Name})");
        }
    }
}

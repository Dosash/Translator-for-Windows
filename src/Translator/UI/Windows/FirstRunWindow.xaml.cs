using System.ComponentModel;
using System.Windows;
using Translator.Core;
using Translator.Platform;

namespace Translator.UI.Windows;

/// <summary>First launch introduction (port of macOS <c>FirstRunView</c>).</summary>
public partial class FirstRunWindow : AuxWindow
{
    private readonly SettingsStore _settings;
    private readonly List<OptionItem> _themes;

    public FirstRunWindow(SettingsStore settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = settings;
        TrayGlyphPath.Data = TrayGlyph.CreateGeometry();

        _themes = Enum.GetValues<AppTheme>()
            .Select(theme => new OptionItem(theme, theme.CaptionKey()) { FixedName = theme.DisplayName() })
            .ToList();
        ThemeChoices.ItemsSource = _themes;
        UpdateThemeSelection();
        UpdateHotkeysCaption();

        settings.PropertyChanged += OnSettingsPropertyChanged;
        settings.HotkeysChanged += OnHotkeysChanged;
        Closed += (_, _) =>
        {
            settings.PropertyChanged -= OnSettingsPropertyChanged;
            settings.HotkeysChanged -= OnHotkeysChanged;
        };
    }

    public event EventHandler? OfflineLanguagesRequested;

    protected override void OnLanguageChanged() => UpdateHotkeysCaption();

    private static string Display(Hotkey hotkey) => hotkey.IsNone ? L10n.T("hotkey.none") : hotkey.Display;

    private void UpdateHotkeysCaption() =>
        HotkeysCaption.Text = L10n.Format("first.hotkeys.caption",
            Display(_settings.SelectionHotkey), Display(_settings.ClipboardHotkey), Display(_settings.PanelHotkey),
            Display(_settings.ScreenHotkey));

    private void UpdateThemeSelection()
    {
        foreach (var item in _themes)
        {
            item.IsSelected = (AppTheme)item.Value == _settings.AppTheme;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsStore.AppTheme))
        {
            UpdateThemeSelection();
        }
    }

    private void OnHotkeysChanged(object? sender, EventArgs e) => UpdateHotkeysCaption();

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OptionItem { Value: AppTheme theme })
        {
            _settings.AppTheme = theme;
        }
    }

    private void OnOpenOfflineClick(object sender, RoutedEventArgs e) => OfflineLanguagesRequested?.Invoke(this, EventArgs.Empty);

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        _settings.CompleteFirstRun();
        Close();
    }
}

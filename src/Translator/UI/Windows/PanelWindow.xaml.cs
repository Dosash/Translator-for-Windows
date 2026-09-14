using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Translator.Core;

namespace Translator.UI.Windows;

/// <summary>The main translator panel above the tray icon (port of macOS <c>TranslatorView</c>).</summary>
public partial class PanelWindow : FloatingWindow
{
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromSeconds(1.2);

    private readonly TranslatorModel _model;
    private readonly SettingsStore _settings;
    private int _copyFeedbackVersion;
    private bool _syncingMode;

    public PanelWindow(TranslatorModel model, SettingsStore settings)
    {
        _model = model;
        _settings = settings;
        InitializeComponent();
        DataContext = model;
        settings.PropertyChanged += OnSettingsPropertyChanged;
        model.PropertyChanged += OnModelPropertyChanged;
        L10n.LanguageChanged += (_, _) =>
        {
            UpdateEngineIndicator();
            ScheduleModeLabelsUpdate();
        };
        Loaded += (_, _) => ScheduleModeLabelsUpdate();
        ApplyPanelSize();
        UpdateModeSwitch();
        UpdateEngineIndicator();
    }

    public event EventHandler? QuitRequested;

    public void FocusInput()
    {
        InputBox.Focus();
        Keyboard.Focus(InputBox);
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    protected override void OnShownPlaced()
    {
        _model.UpdateOfflineFooter();
        ScheduleModeLabelsUpdate();
        FocusInput();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _model.Translate();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void ApplyPanelSize()
    {
        var mode = _settings.PanelSizeMode;
        Width = mode.PanelWidth();
        InputBox.Height = mode.TextAreaHeight();
        ResultBox.Height = mode.ResultHeight();
        ScheduleModeLabelsUpdate();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsStore.PanelSizeMode))
        {
            ApplyPanelSize();
        }
        else if (e.PropertyName == nameof(SettingsStore.OfflineOnly))
        {
            UpdateModeSwitch();
            UpdateEngineIndicator();
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TranslatorModel.Engine) or nameof(TranslatorModel.OutputText))
        {
            UpdateEngineIndicator();
        }
    }

    private void UpdateModeSwitch()
    {
        _syncingMode = true;
        OnlineMode.IsChecked = !_settings.OfflineOnly;
        OfflineMode.IsChecked = _settings.OfflineOnly;
        _syncingMode = false;
    }

    private void OnOnlineModeChecked(object sender, RoutedEventArgs e) => SetOfflineOnly(false);

    private void OnOfflineModeChecked(object sender, RoutedEventArgs e) => SetOfflineOnly(true);

    private void SetOfflineOnly(bool offlineOnly)
    {
        if (!_syncingMode)
        {
            // The model reacts to the setting: it refreshes the offline footer and translates again.
            _settings.OfflineOnly = offlineOnly;
        }
    }

    private void UpdateEngineIndicator()
    {
        var content = PrivacyCapsule.Describe(_model.Engine, _settings.OfflineOnly);
        EngineIcon.Text = content.Glyph;
        EngineName.Text = content.Label ?? string.Empty;
        EngineIndicator.ToolTip = _model.EnginePrivacyText;
        AutomationProperties.SetName(EngineIndicator, _model.EnginePrivacyText);
        EngineIndicator.Visibility = _model.Engine != EngineKind.None && !string.IsNullOrEmpty(_model.OutputText)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ScheduleModeLabelsUpdate() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateModeLabels);

    /// <summary>
    /// The mode switch keeps only its icons (and tooltips) when the labels would squeeze the title:
    /// in the compact panel and for long translations of "Online"/"Offline".
    /// </summary>
    private void UpdateModeLabels()
    {
        if (HeaderGrid.ActualWidth <= 0)
        {
            return;
        }
        SetModeLabelsVisible(true);
        // Measure results are cached per constraint, so the switch must go through a layout pass with its labels back.
        UpdateLayout();
        TitleStack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var needed = Logo.ActualWidth + TitleStack.DesiredSize.Width + ModeSwitch.DesiredSize.Width;
        SetModeLabelsVisible(needed <= HeaderGrid.ActualWidth);
    }

    private void SetModeLabelsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        OnlineLabel.Visibility = visibility;
        OfflineLabel.Visibility = visibility;
    }

    private void OnSwapClick(object sender, RoutedEventArgs e) => _model.SwapLanguages();

    private void OnTranslateClick(object sender, RoutedEventArgs e) => _model.Translate();

    private void OnSpeakInputClick(object sender, RoutedEventArgs e) => _model.SpeakInput();

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _model.Clear();
        FocusInput();
    }

    private void OnSpeakOutputClick(object sender, RoutedEventArgs e) => _model.SpeakOutput();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        _model.CopyResult();
        _ = ShowCopyFeedbackAsync(CopyButton);
    }

    private void OnLaterClick(object sender, RoutedEventArgs e) => _model.DismissOfflineUpdateNotice();

    private void OnOfflineLanguagesClick(object sender, RoutedEventArgs e) => _model.RequestOfflineLanguages();

    private void OnHistoryClick(object sender, RoutedEventArgs e) => _model.RequestHistory();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _model.RequestSettings();

    private void OnQuitClick(object sender, RoutedEventArgs e) => QuitRequested?.Invoke(this, EventArgs.Empty);

    private async Task ShowCopyFeedbackAsync(Button button)
    {
        var version = ++_copyFeedbackVersion;
        button.Content = Icons.Check;
        await Task.Delay(CopyFeedbackDuration);
        if (version == _copyFeedbackVersion)
        {
            button.Content = Icons.Copy;
        }
    }
}

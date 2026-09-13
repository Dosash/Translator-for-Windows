using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Translator.Core;

namespace Translator.UI.Windows;

/// <summary>The main translator panel above the tray icon (port of macOS <c>TranslatorView</c>).</summary>
public partial class PanelWindow : FloatingWindow
{
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromSeconds(1.2);

    private readonly TranslatorModel _model;
    private readonly SettingsStore _settings;
    private int _copyFeedbackVersion;

    public PanelWindow(TranslatorModel model, SettingsStore settings)
    {
        _model = model;
        _settings = settings;
        InitializeComponent();
        DataContext = model;
        settings.PropertyChanged += OnSettingsPropertyChanged;
        ApplyPanelSize();
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
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsStore.PanelSizeMode))
        {
            ApplyPanelSize();
        }
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

using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Translator.Core;
using Translator.Platform;

namespace Translator.UI.Windows;

/// <summary>Where the text in the bubble came from.</summary>
public enum BubbleSource
{
    /// <summary>Selected text copied from another app: "Replace" pastes the translation back.</summary>
    Selection,
    /// <summary>Text recognized in a screen area: nothing to replace; the recognized original can be shown.</summary>
    Screen,
}

/// <summary>Compact translation bubble next to the selected text or screen area (port of macOS <c>BubbleView</c>).</summary>
public partial class BubbleWindow : FloatingWindow
{
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromSeconds(1.2);

    private readonly TranslatorModel _model;
    private int _copyFeedbackVersion;
    private BubbleSource _source = BubbleSource.Selection;
    private bool _recognizing;
    private bool _hasNotice;
    private bool _showOriginal;

    public BubbleWindow(TranslatorModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += OnModelPropertyChanged;
        L10n.LanguageChanged += (_, _) => UpdateContentState();
        UpdateContentState();
    }

    public event EventHandler? ReplaceRequested;

    public event EventHandler? ExpandRequested;

    public event EventHandler? LanguageSettingsRequested;

    private bool HasOriginal =>
        _source == BubbleSource.Screen && !_recognizing && !_hasNotice && !string.IsNullOrWhiteSpace(_model.InputText);

    private bool ShowingOriginal => _showOriginal && HasOriginal;

    /// <summary>Resets the bubble for a new result coming from <paramref name="source"/>.</summary>
    public void Configure(BubbleSource source)
    {
        _source = source;
        _recognizing = false;
        _hasNotice = false;
        _showOriginal = false;
        UpdateContentState();
    }

    /// <summary>Screen area: "Recognizing text…" until the recognized text is handed to the model.</summary>
    public void SetRecognizing(bool recognizing)
    {
        _recognizing = recognizing;
        UpdateContentState();
    }

    /// <summary>A message instead of a translation, optionally with a link to the Windows language settings.</summary>
    public void ShowNotice(string message, string? hint, bool offerLanguageSettings)
    {
        _hasNotice = true;
        _recognizing = false;
        _showOriginal = false;
        NoticeText.Text = message;
        NoticeHint.Text = hint ?? string.Empty;
        NoticeHint.Visibility = VisibleIf(hint is not null);
        LanguageSettingsButton.Visibility = VisibleIf(offerLanguageSettings);
        UpdateContentState();
    }

    protected override void OnShownPlaced()
    {
        // Keyboard focus inside the bubble so Esc and Enter (Replace) work immediately.
        Focus();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TranslatorModel.ErrorMessage) or nameof(TranslatorModel.OutputText)
            or nameof(TranslatorModel.Engine) or nameof(TranslatorModel.InputText))
        {
            UpdateContentState();
        }
    }

    private void UpdateContentState()
    {
        var screen = _source == BubbleSource.Screen;
        var busy = _recognizing || _hasNotice;
        var original = ShowingOriginal;
        var translationView = !busy && !original;
        var hasError = !string.IsNullOrEmpty(_model.ErrorMessage);
        var hasOutput = !string.IsNullOrEmpty(_model.OutputText);

        NoticePanel.Visibility = VisibleIf(_hasNotice);
        ErrorText.Visibility = VisibleIf(translationView && hasError);
        ProgressRow.Visibility = VisibleIf(_recognizing || (translationView && !hasError && !hasOutput));
        ProgressText.Text = L10n.T(_recognizing ? "ocr.recognizing" : "translating");
        OutputBox.Visibility = VisibleIf(translationView && !hasError && hasOutput);
        OriginalBox.Visibility = VisibleIf(original);
        EngineText.Visibility = VisibleIf(translationView && !hasError && hasOutput && _model.Engine != EngineKind.None);

        ActionsRow.Visibility = VisibleIf(!busy);
        ReplaceButton.Visibility = VisibleIf(!screen);
        ReplaceButton.IsDefault = !screen;
        OriginalToggle.Visibility = VisibleIf(HasOriginal);
        SetLabel(OriginalToggle, L10n.T(original ? "ocr.translation.help" : "ocr.original.help"));
        OriginalToggle.Content = L10n.T(original ? "ocr.translation" : "ocr.original");
        SpeakButton.Margin = new Thickness(screen && !HasOriginal ? 0 : 8, 0, 0, 0);

        var shown = original ? _model.InputText : _model.OutputText;
        SpeakButton.IsEnabled = !string.IsNullOrEmpty(shown);
        CopyButton.IsEnabled = !string.IsNullOrEmpty(shown);
        SetLabel(SpeakButton, L10n.T(original ? "speak.source" : "speak.translation"));
        SetLabel(CopyButton, L10n.T(original ? "ocr.copy.original" : "copy.translation"));

        if (OutputBox.Visibility == Visibility.Visible)
        {
            OutputBox.ScrollToHome();
        }
        if (OriginalBox.Visibility == Visibility.Visible)
        {
            OriginalBox.ScrollToHome();
        }
    }

    private static Visibility VisibleIf(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static void SetLabel(Button button, string text)
    {
        button.ToolTip = text;
        AutomationProperties.SetName(button, text);
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e) => ReplaceRequested?.Invoke(this, EventArgs.Empty);

    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);

    private void OnLanguageSettingsClick(object sender, RoutedEventArgs e) => LanguageSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnOriginalToggleClick(object sender, RoutedEventArgs e)
    {
        _showOriginal = !ShowingOriginal;
        UpdateContentState();
    }

    private void OnSpeakClick(object sender, RoutedEventArgs e)
    {
        if (ShowingOriginal)
        {
            _model.SpeakInput();
        }
        else
        {
            _model.SpeakOutput();
        }
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (ShowingOriginal)
        {
            ClipboardService.SetText(_model.InputText);
        }
        else
        {
            _model.CopyResult();
        }
        var version = ++_copyFeedbackVersion;
        CopyButton.Content = Icons.Check;
        await Task.Delay(CopyFeedbackDuration);
        if (version == _copyFeedbackVersion)
        {
            CopyButton.Content = Icons.Copy;
        }
    }
}

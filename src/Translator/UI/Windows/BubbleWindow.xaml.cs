using System.ComponentModel;
using System.Windows;
using Translator.Core;

namespace Translator.UI.Windows;

/// <summary>Compact translation bubble next to the selected text (port of macOS <c>BubbleView</c>).</summary>
public partial class BubbleWindow : FloatingWindow
{
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromSeconds(1.2);

    private readonly TranslatorModel _model;
    private int _copyFeedbackVersion;

    public BubbleWindow(TranslatorModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += OnModelPropertyChanged;
        UpdateContentState();
    }

    public event EventHandler? ReplaceRequested;

    public event EventHandler? ExpandRequested;

    protected override void OnShownPlaced()
    {
        // Keyboard focus inside the bubble so Esc and Enter (Replace) work immediately.
        Focus();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TranslatorModel.ErrorMessage) or nameof(TranslatorModel.OutputText) or nameof(TranslatorModel.Engine))
        {
            UpdateContentState();
        }
    }

    private void UpdateContentState()
    {
        var hasError = !string.IsNullOrEmpty(_model.ErrorMessage);
        var hasOutput = !string.IsNullOrEmpty(_model.OutputText);
        ErrorText.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
        ProgressRow.Visibility = !hasError && !hasOutput ? Visibility.Visible : Visibility.Collapsed;
        OutputBox.Visibility = !hasError && hasOutput ? Visibility.Visible : Visibility.Collapsed;
        EngineText.Visibility = !hasError && hasOutput && _model.Engine != EngineKind.None ? Visibility.Visible : Visibility.Collapsed;
        if (OutputBox.Visibility == Visibility.Visible)
        {
            OutputBox.ScrollToHome();
        }
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e) => ReplaceRequested?.Invoke(this, EventArgs.Empty);

    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);

    private void OnSpeakClick(object sender, RoutedEventArgs e) => _model.SpeakOutput();

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        _model.CopyResult();
        var version = ++_copyFeedbackVersion;
        CopyButton.Content = Icons.Check;
        await Task.Delay(CopyFeedbackDuration);
        if (version == _copyFeedbackVersion)
        {
            CopyButton.Content = Icons.Copy;
        }
    }
}

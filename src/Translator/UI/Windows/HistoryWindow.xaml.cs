using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Translator.Core;
using Translator.Platform;

namespace Translator.UI.Windows;

/// <summary>Last translations (port of macOS <c>HistoryView</c>).</summary>
public partial class HistoryWindow : AuxWindow
{
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromSeconds(1.2);

    private readonly TranslatorModel _model;

    public HistoryWindow(TranslatorModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        UpdateSubtitle();
    }

    protected override void OnLanguageChanged()
    {
        UpdateSubtitle();
        // Pair, engine and date texts are computed from the current UI language; re-create the cards.
        CollectionViewSource.GetDefaultView(_model.History).Refresh();
    }

    private void UpdateSubtitle() => SubtitleText.Text = L10n.Format("history.subtitle", TranslatorModel.HistoryLimit);

    private void OnClearClick(object sender, RoutedEventArgs e) => _model.ClearHistory();

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HistoryEntry entry } button)
        {
            return;
        }
        ClipboardService.SetText(entry.Output);
        button.Content = Icons.Check;
        await Task.Delay(CopyFeedbackDuration);
        button.Content = Icons.Copy;
    }

    private void OnScrollerPreviewMouseWheel(object sender, MouseWheelEventArgs e) => ForwardMouseWheel(Scroller, e);
}

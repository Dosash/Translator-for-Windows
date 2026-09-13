using System.ComponentModel;
using System.Windows;
using Translator.Core;
using Translator.Offline;

namespace Translator.UI.Windows;

/// <summary>Download, update and remove offline languages (port of macOS <c>DownloadView</c>).</summary>
public partial class OfflineLanguagesWindow : AuxWindow
{
    private readonly TranslatorModel _model;
    private readonly SettingsStore _settings;
    private readonly OfflineModelManager _manager;
    private readonly List<OfflineLanguageRow> _rows;
    private OfflineLanguageRow? _busyRow;
    private CancellationTokenSource? _downloadCts;

    public OfflineLanguagesWindow(TranslatorModel model, SettingsStore settings, OfflineModelManager manager)
    {
        _model = model;
        _settings = settings;
        _manager = manager;
        InitializeComponent();
        DataContext = model;

        _rows = OfflineModelManager.SupportedLanguages.Select(code => new OfflineLanguageRow(code)).ToList();
        Rows.ItemsSource = _rows;
        EnglishName.Text = L10n.LanguageName(OfflineModelManager.BaseLanguage);
        RefreshRows();

        manager.StateChanged += OnManagerStateChanged;
        model.PropertyChanged += OnModelPropertyChanged;
        Closed += (_, _) =>
        {
            manager.StateChanged -= OnManagerStateChanged;
            model.PropertyChanged -= OnModelPropertyChanged;
            _downloadCts?.Cancel();
        };
        Loaded += async (_, _) => await RefreshModelStatusAsync();
    }

    protected override void OnLanguageChanged()
    {
        EnglishName.Text = L10n.LanguageName(OfflineModelManager.BaseLanguage);
        foreach (var row in _rows)
        {
            row.RefreshTexts();
        }
    }

    private void RefreshRows()
    {
        foreach (var row in _rows)
        {
            row.State = SafeGetState(row.Code);
            row.UpdateFlagged = _model.OfflineUpdateCodes.Contains(row.Code);
            row.SizeBytes = SafeGetSize(row.Code);
            row.IsDownloading = ReferenceEquals(row, _busyRow);
            row.IsAnyBusy = _busyRow is not null;
        }
    }

    private OfflineLanguageState SafeGetState(string code)
    {
        try
        {
            return _manager.GetState(code);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OfflineLanguagesWindow: GetState failed ({ex.GetType().Name})");
            return OfflineLanguageState.NotInstalled;
        }
    }

    private long SafeGetSize(string code)
    {
        try
        {
            return _manager.GetDownloadSizeBytes(code);
        }
        catch
        {
            return 0;
        }
    }

    private void OnManagerStateChanged(object? sender, string code) =>
        // Raised on a background thread.
        Dispatcher.BeginInvoke(() =>
        {
            if (_rows.FirstOrDefault(r => r.Code == code) is { } row)
            {
                row.State = SafeGetState(code);
            }
        });

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslatorModel.OfflineUpdateCodes))
        {
            RefreshRows();
        }
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OfflineLanguageRow row || _busyRow is not null)
        {
            return;
        }

        _busyRow = row;
        row.Progress = 0;
        var cts = new CancellationTokenSource();
        _downloadCts = cts;
        ShowMessage(null, isError: false);
        RefreshRows();

        var progress = new Progress<OfflineDownloadProgress>(p =>
        {
            if (p.TotalBytes is long total && total > 0)
            {
                row.Progress = Math.Clamp(p.BytesReceived * 100.0 / total, 0, 100);
            }
        });

        try
        {
            await _manager.DownloadAsync(row.Code, progress, cts.Token);
            ShowMessage(L10n.T("offline.download.done"), isError: false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user or by closing the window.
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OfflineLanguagesWindow: download failed ({ex.GetType().Name})");
            ShowMessage(L10n.Format("offline.download.failed", ex.Message), isError: true);
        }
        finally
        {
            _busyRow = null;
            _downloadCts = null;
            cts.Dispose();
            RefreshRows();
        }
        await AfterInstalledSetChangedAsync();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _downloadCts?.Cancel();

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OfflineLanguageRow row || _busyRow is not null)
        {
            return;
        }
        try
        {
            await _manager.RemoveAsync(row.Code);
            ShowMessage(null, isError: false);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OfflineLanguagesWindow: remove failed ({ex.GetType().Name})");
            ShowMessage(L10n.Format("error.translate.failed", ex.Message), isError: true);
        }
        RefreshRows();
        await AfterInstalledSetChangedAsync();
    }

    private void OnLaterClick(object sender, RoutedEventArgs e) => _model.DismissOfflineUpdateNotice();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowMessage(string? message, bool isError)
    {
        MessageText.Text = message ?? string.Empty;
        MessageText.SetResourceReference(ForegroundProperty, isError ? "RoseInkBrush" : "SageBrush");
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Records what is installed now (like macOS recordSnapshot), so a deliberate removal isn't later
    /// reported as "needs update", and refreshes the panel's offline footer.
    /// </summary>
    private async Task AfterInstalledSetChangedAsync()
    {
        try
        {
            _settings.OfflineInstalledSnapshot = OfflineModelManager.SupportedLanguages
                .Where(code => SafeGetState(code) is OfflineLanguageState.Installed or OfflineLanguageState.UpdateAvailable)
                .ToList();
            if (_model.OfflineUpdateCodes.Count > 0 &&
                _model.OfflineUpdateCodes.All(code => SafeGetState(code) == OfflineLanguageState.Installed))
            {
                _model.DismissOfflineUpdateNotice();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OfflineLanguagesWindow: snapshot update failed ({ex.GetType().Name})");
        }
        await RefreshModelStatusAsync();
    }

    private async Task RefreshModelStatusAsync()
    {
        try
        {
            await _model.RefreshOfflineStatusAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OfflineLanguagesWindow: status refresh failed ({ex.GetType().Name})");
        }
        RefreshRows();
    }
}

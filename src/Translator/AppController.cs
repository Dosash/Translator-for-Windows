using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Translator.Core;
using Translator.Offline;
using Translator.Platform;
using Translator.UI;
using Translator.UI.Shell;
using Translator.UI.Windows;

namespace Translator;

/// <summary>
/// Port of the macOS AppDelegate: owns the tray icon, global hotkeys, the panel and bubble, and the
/// auxiliary windows, and wires them to the Core model.
/// </summary>
public sealed class AppController : IDisposable
{
    private static readonly TimeSpan OfflineCheckTimerInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RefocusDelay = TimeSpan.FromMilliseconds(120);

    private readonly Application _app;
    private readonly SettingsStore _settings;
    private readonly OfflineModelManager _offlineManager;
    private readonly SpeechService _speech;
    private readonly TranslatorModel _model;
    private readonly AuxWindowTracker _auxTracker = new();

    private PanelWindow? _panel;
    private BubbleWindow? _bubble;
    private TrayIcon? _trayIcon;
    private TrayMenu? _trayMenu;
    private HotkeyManager? _hotkeys;
    private ForegroundTracker? _foreground;
    private DispatcherTimer? _offlineCheckTimer;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private FirstRunWindow? _firstRunWindow;
    private OfflineLanguagesWindow? _offlineWindow;
    private IntPtr _selectionSourceWindow;
    private PixelRect _bubbleAnchor;
    private bool _selectionInProgress;
    private bool _disposed;

    public AppController(Application app)
    {
        _app = app;
        _settings = SettingsStore.Load();
        var history = HistoryStore.Load();
        _offlineManager = new OfflineModelManager(AppPaths.ModelsDirectory);
        _speech = new SpeechService();
        _model = new TranslatorModel(
            _settings,
            history,
            new GoogleTranslateEngine(),
            _offlineManager,
            new OfflineModelManagerStatusSource(_offlineManager),
            _speech,
            _offlineManager);
    }

    private enum PanelAnchor
    {
        /// <summary>Centered above the tray icon (tray click, tray menu).</summary>
        Tray,
        /// <summary>Tray corner of the monitor under the cursor (hotkeys, --translate, expand).</summary>
        Cursor,
    }

    public void Start(CliCommand command)
    {
        ThemeManager.Initialize(_app.Dispatcher);
        ThemeManager.Apply(_settings.AppTheme);

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _settings.HotkeysChanged += OnHotkeysChanged;
        _settings.RecordingStateChanged += OnRecordingStateChanged;
        L10n.LanguageChanged += OnLanguageChanged;

        _model.SettingsRequested += (_, _) => ShowSettings();
        _model.HistoryRequested += (_, _) => ShowHistory();
        _model.OfflineLanguagesRequested += (_, _) => ShowOfflineLanguages();

        _panel = new PanelWindow(_model, _settings);
        _panel.QuitRequested += (_, _) => Quit();
        _bubble = new BubbleWindow(_model);
        _bubble.ReplaceRequested += (_, _) => _ = ReplaceSelectionAsync();
        _bubble.ExpandRequested += (_, _) =>
        {
            _bubble.HideWindow();
            ShowPanel(PanelAnchor.Cursor);
        };

        _foreground = new ForegroundTracker();
        _hotkeys = new HotkeyManager();
        RegisterHotkeys();

        _trayMenu = new TrayMenu();
        _trayIcon = new TrayIcon();
        _trayIcon.SetTooltip(L10n.T("app.title"));
        // Leave the tray window procedure before opening windows or menus.
        _trayIcon.LeftClick += (_, _) => _app.Dispatcher.BeginInvoke(TogglePanelFromTray);
        _trayIcon.RightClick += (_, _) => _app.Dispatcher.BeginInvoke(ShowTrayMenu);
        _trayIcon.Show();

        _model.SchedulePeriodicOfflineUpdateCheck();
        _offlineCheckTimer = new DispatcherTimer(OfflineCheckTimerInterval, DispatcherPriority.Background,
            (_, _) => _model.SchedulePeriodicOfflineUpdateCheck(), _app.Dispatcher);

        if (!_settings.HasCompletedFirstRun)
        {
            ShowFirstRun();
        }
        HandleCommand(command, fromAnotherInstance: false);
    }

    /// <summary>Arguments forwarded by a second launch (already on the UI thread).</summary>
    public void HandleArguments(string[] args) => HandleCommand(CommandLine.Parse(args), fromAnotherInstance: true);

    public void Quit()
    {
        Dispose();
        _app.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DebugLog.Write("AppController: shutting down");

        _offlineCheckTimer?.Stop();
        L10n.LanguageChanged -= OnLanguageChanged;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _settings.HotkeysChanged -= OnHotkeysChanged;
        _settings.RecordingStateChanged -= OnRecordingStateChanged;

        foreach (var window in AuxWindows().ToList())
        {
            window.Closed -= OnAuxWindowClosed;
            window.Close();
        }
        _trayMenu?.Dispose();
        _hotkeys?.Dispose();
        _foreground?.Dispose();
        _trayIcon?.Dispose();
        _bubble?.CloseForQuit();
        _panel?.CloseForQuit();
        _speech.Stop();
        _speech.Dispose();
        _offlineManager.Dispose();
    }

    private void HandleCommand(CliCommand command, bool fromAnotherInstance)
    {
        switch (command.Kind)
        {
            case CliCommandKind.Translate:
                ShowPanel(PanelAnchor.Cursor);
                var text = command.Text?.Trim() ?? string.Empty;
                if (text.Length > 0)
                {
                    _model.InputText = text;
                    _model.Translate();
                }
                break;
            case CliCommandKind.Normal when fromAnotherInstance:
                ShowPanel(PanelAnchor.Cursor);
                break;
        }
    }

    // ---- Panel and bubble ---------------------------------------------------

    private void TogglePanelFromTray()
    {
        if (_panel is null)
        {
            return;
        }
        if (_panel.IsVisible)
        {
            _panel.HideWindow();
        }
        else if (!_panel.JustHidByOutsideClick)
        {
            // Otherwise the click on the icon already closed the panel via deactivation: don't reopen it.
            ShowPanel(PanelAnchor.Tray);
        }
    }

    private void TogglePanelFromHotkey()
    {
        if (_panel is null)
        {
            return;
        }
        if (_panel.IsVisible && _panel.IsActive)
        {
            _panel.HideWindow();
        }
        else if (_panel.IsVisible)
        {
            _panel.ActivateWindow();
            _panel.FocusInput();
        }
        else
        {
            ShowPanel(PanelAnchor.Cursor);
        }
    }

    private void ShowPanel(PanelAnchor anchor)
    {
        if (_panel is null || _disposed)
        {
            return;
        }
        _bubble?.HideWindow();
        var iconRect = anchor == PanelAnchor.Tray ? _trayIcon?.GetIconRect() : null;
        if (iconRect is { Width: <= 0 } or { Height: <= 0 })
        {
            iconRect = null;
        }
        var cursorRect = SelectionLocator.GetMouseRect();
        _panel.ShowPlaced(size => PlaceAtTray(size, iconRect, cursorRect));
    }

    private void ShowBubble(PixelRect anchor)
    {
        _bubbleAnchor = anchor;
        _bubble?.ShowPlaced(size =>
        {
            var scale = ScreenInfo.GetScale(anchor);
            return WindowPlacement.PlaceNearAnchor(size, anchor, ScreenInfo.GetWorkArea(anchor), Scaled(10, scale), Scaled(12, scale));
        });
    }

    private static PixelPoint PlaceAtTray(PixelSize size, PixelRect? iconRect, PixelRect cursorRect)
    {
        var near = iconRect ?? cursorRect;
        var workArea = ScreenInfo.GetWorkArea(near);
        var edge = ScreenInfo.GetTaskbarEdge(ScreenInfo.GetMonitorBounds(near), workArea);
        var scale = ScreenInfo.GetScale(near);
        return WindowPlacement.PlaceAtTray(size, iconRect, workArea, edge, Scaled(10, scale), Scaled(12, scale));
    }

    private static int Scaled(int value, double scale) => (int)Math.Round(value * scale);

    // ---- Hotkey actions -----------------------------------------------------

    /// <summary>Port of macOS translateSelection: grab the selection with Ctrl+C and show the bubble next to it.</summary>
    private async Task TranslateSelectionAsync(IntPtr? sourceWindow = null)
    {
        if (_selectionInProgress || _panel is null || _bubble is null || _disposed)
        {
            return;
        }
        _selectionInProgress = true;
        try
        {
            var target = sourceWindow ?? ForegroundWindow.Get();
            _selectionSourceWindow = target;

            if (!ProcessElevation.IsCurrentProcessElevated() && ProcessElevation.IsWindowElevated(target) == true)
            {
                DebugLog.Write("Selection: foreground window is elevated");
                var mouseAnchor = SelectionLocator.GetMouseRect();
                _panel.HideWindow();
                _model.PrepareForSelection();
                _model.ShowError(L10n.T("hotkey.admin.note"));
                ShowBubble(mouseAnchor);
                return;
            }

            string? selected;
            try
            {
                selected = (await SelectionGrabber.GrabSelectedTextAsync())?.Trim();
            }
            catch (Exception ex)
            {
                // Shown to the user as "nothing selected", so keep the real cause in the log.
                DebugLog.Write($"Selection: grab failed ({ex.GetType().Name}){Environment.NewLine}{ex.StackTrace}");
                selected = null;
            }

            var anchor = SelectionLocator.GetAnchor();
            _panel.HideWindow();
            _model.PrepareForSelection();
            if (string.IsNullOrEmpty(selected))
            {
                _model.ShowError(L10n.T("error.selection.empty"));
                ShowBubble(anchor);
                return;
            }
            _model.InputText = selected;
            _model.Translate();
            ShowBubble(anchor);
        }
        finally
        {
            _selectionInProgress = false;
        }
    }

    private async Task TranslateSelectionFromMenuAsync()
    {
        // The tray click moved the foreground to the taskbar; go back to where the selection is.
        var target = _foreground?.LastExternalWindow ?? IntPtr.Zero;
        if (target != IntPtr.Zero)
        {
            ForegroundWindow.Activate(target);
            await Task.Delay(RefocusDelay);
        }
        await TranslateSelectionAsync(target == IntPtr.Zero ? null : target);
    }

    /// <summary>"Replace" in the bubble: refocus the source window and paste the translation over the selection.</summary>
    private async Task ReplaceSelectionAsync()
    {
        var translation = _model.OutputText;
        if (string.IsNullOrEmpty(translation) || _bubble is null)
        {
            return;
        }
        _bubble.HideWindow();
        bool pasted;
        try
        {
            pasted = await TextInserter.PasteAsync(translation, _selectionSourceWindow);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Replace: paste failed ({ex.GetType().Name}){Environment.NewLine}{ex.StackTrace}");
            pasted = false;
        }
        if (!pasted && !_disposed)
        {
            // Bring the bubble back with the reason; the translation stays there to copy or retry.
            _model.ShowError(L10n.T("error.replace.failed"));
            ShowBubble(_bubbleAnchor);
        }
    }

    private void TranslateClipboard(PanelAnchor anchor)
    {
        ShowPanel(anchor);
        var text = ClipboardService.GetText()?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            _model.ShowError(L10n.T("error.clipboard.empty"));
            return;
        }
        _model.InputText = text;
        _model.Translate();
    }

    private void RegisterHotkeys()
    {
        if (_disposed || _hotkeys is null)
        {
            return;
        }
        // Unregister everything first so swapping two shortcuts never collides with our own registration.
        _hotkeys.UnregisterAll();
        if (_settings.IsRecordingHotkey)
        {
            return;
        }

        string? failed = null;
        void Register(int id, Hotkey hotkey, Action action)
        {
            if (!_hotkeys.Register(id, hotkey, () => _app.Dispatcher.BeginInvoke(action)))
            {
                failed ??= hotkey.Display;
            }
        }

        Register(HotkeyManager.SelectionId, _settings.SelectionHotkey, () => _ = TranslateSelectionAsync());
        Register(HotkeyManager.ClipboardId, _settings.ClipboardHotkey, () => TranslateClipboard(PanelAnchor.Cursor));
        Register(HotkeyManager.PanelId, _settings.PanelHotkey, TogglePanelFromHotkey);
        _settings.HotkeyError = failed is null ? null : L10n.Format("hotkey.taken", failed);
    }

    // ---- Tray menu ----------------------------------------------------------

    private void ShowTrayMenu()
    {
        if (_trayMenu is null || _disposed)
        {
            return;
        }
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem(L10n.T("open.panel"), Hotkey.None, () => ShowPanel(PanelAnchor.Tray)));
        menu.Items.Add(CreateMenuItem(L10n.T("hotkey.selection"), _settings.SelectionHotkey, () => _ = TranslateSelectionFromMenuAsync()));
        menu.Items.Add(CreateMenuItem(L10n.T("hotkey.clipboard"), _settings.ClipboardHotkey, () => TranslateClipboard(PanelAnchor.Tray)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(L10n.T("history"), Hotkey.None, ShowHistory));
        menu.Items.Add(CreateMenuItem(L10n.T("settings"), Hotkey.None, ShowSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(L10n.T("quit"), Hotkey.None, Quit));
        _trayMenu.Show(menu);
    }

    private MenuItem CreateMenuItem(string header, Hotkey hotkey, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGestureText = hotkey.IsNone ? string.Empty : hotkey.Display,
        };
        item.Click += (_, _) =>
        {
            _trayMenu?.Close();
            _app.Dispatcher.BeginInvoke(action, DispatcherPriority.Input);
        };
        return item;
    }

    // ---- Auxiliary windows --------------------------------------------------

    private void ShowSettings() => ShowAux(ref _settingsWindow, () => new SettingsWindow(_settings));

    private void ShowHistory() => ShowAux(ref _historyWindow, () => new HistoryWindow(_model));

    private void ShowOfflineLanguages() =>
        ShowAux(ref _offlineWindow, () => new OfflineLanguagesWindow(_model, _settings, _offlineManager));

    private void ShowFirstRun() => ShowAux(ref _firstRunWindow, () =>
    {
        var window = new FirstRunWindow(_settings);
        window.OfflineLanguagesRequested += (_, _) => ShowOfflineLanguages();
        return window;
    });

    /// <summary>Single instance per kind; the panel stays visible behind (macOS rememberPanelBeforeAux).</summary>
    private void ShowAux<T>(ref T? field, Func<T> create) where T : AuxWindow
    {
        if (_disposed)
        {
            return;
        }
        _auxTracker.BeforeAuxShown(_panel?.IsVisible == true);
        if (_panel is not null)
        {
            _panel.KeepsVisibleBehindOtherWindows = true;
        }
        if (field is null)
        {
            var window = create();
            window.Closed += OnAuxWindowClosed;
            field = window;
        }
        field.BringToFront();
    }

    private IEnumerable<AuxWindow> AuxWindows() =>
        new AuxWindow?[] { _settingsWindow, _historyWindow, _offlineWindow, _firstRunWindow }.OfType<AuxWindow>();

    private void OnAuxWindowClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _settingsWindow))
        {
            _settingsWindow = null;
        }
        else if (ReferenceEquals(sender, _historyWindow))
        {
            _historyWindow = null;
        }
        else if (ReferenceEquals(sender, _offlineWindow))
        {
            _offlineWindow = null;
        }
        else if (ReferenceEquals(sender, _firstRunWindow))
        {
            _firstRunWindow = null;
        }
        if (_disposed || _panel is null)
        {
            return;
        }

        var decision = _auxTracker.AuxClosed(AuxWindows().Any(w => w.IsVisible), _panel.IsVisible);
        if (decision.RestorePanelLevel)
        {
            _panel.KeepsVisibleBehindOtherWindows = false;
            if (_panel.IsVisible)
            {
                // Active again, so the next click elsewhere hides it as usual.
                _panel.ActivateWindow();
            }
        }
        if (decision.ShowPanel)
        {
            ShowPanel(PanelAnchor.Tray);
        }
    }

    // ---- Settings and language ---------------------------------------------

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsStore.AppTheme))
        {
            ThemeManager.Apply(_settings.AppTheme);
        }
    }

    private void OnHotkeysChanged(object? sender, EventArgs e) => RegisterHotkeys();

    private void OnRecordingStateChanged(object? sender, bool isRecording) => RegisterHotkeys();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _trayIcon?.SetTooltip(L10n.T("app.title"));
        if (_settings.HotkeyError is not null)
        {
            RegisterHotkeys();
        }
    }
}

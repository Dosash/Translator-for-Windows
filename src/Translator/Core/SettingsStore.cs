using System.IO;
using System.Text.Json;
using Translator.Platform;

namespace Translator.Core;

/// <summary>
/// Persisted app settings, backed by <see cref="AppPaths.SettingsFile"/> (System.Text.Json, atomic write).
/// Every persisted property saves to disk on change. Load is tolerant of a missing/corrupt file.
/// </summary>
public sealed class SettingsStore : ObservableObject
{
    private const string DefaultTargetForEnglishSystem = "es";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    private Hotkey _selectionHotkey = Hotkey.DefaultSelection;
    private Hotkey _clipboardHotkey = Hotkey.DefaultClipboard;
    private Hotkey _panelHotkey = Hotkey.DefaultPanel;
    private Hotkey _screenHotkey = Hotkey.DefaultScreen;
    private bool _autoTranslate = true;
    private bool _offlineOnly;
    private AppTheme _appTheme = AppTheme.CalmGlass;
    private PanelSizeMode _panelSizeMode = PanelSizeMode.Standard;
    private AppUILanguage _appLanguage = AppUILanguage.System;
    private bool _hasCompletedFirstRun;
    private string _sourceCode = "auto";
    private string _targetCode = DefaultTargetCode();
    private DateTimeOffset? _offlineUpdateLastCheck;
    private List<string> _offlineInstalledSnapshot = [];
    private bool _launchAtLogin;

    private string? _loginItemMessage;
    private string? _updateMessage;
    private string? _updateUrl;
    private bool _isCheckingForUpdates;
    private bool _isRecordingHotkey;
    private string? _hotkeyError;

    public event EventHandler? HotkeysChanged;
    public event EventHandler<bool>? RecordingStateChanged;

    private readonly Action<bool> _setAutostartEnabled;

    public SettingsStore(string path)
        : this(path, Autostart.IsEnabled, Autostart.SetEnabled)
    {
    }

    /// <summary>Tests pass their own autostart delegates so the real HKCU Run key is never touched.</summary>
    internal SettingsStore(string path, Func<bool> isAutostartEnabled, Action<bool> setAutostartEnabled)
    {
        _path = path;
        _setAutostartEnabled = setAutostartEnabled;
        var data = TryLoad(path);
        if (data is not null)
        {
            ApplyData(data);
        }
        try
        {
            _launchAtLogin = isAutostartEnabled();
        }
        catch
        {
            _launchAtLogin = false;
        }
        L10n.Selected = _appLanguage;
    }

    public static SettingsStore Load() => new(AppPaths.SettingsFile);

    public Hotkey SelectionHotkey
    {
        get => _selectionHotkey;
        set { if (SetField(ref _selectionHotkey, value)) { Save(); HotkeysChanged?.Invoke(this, EventArgs.Empty); } }
    }

    public Hotkey ClipboardHotkey
    {
        get => _clipboardHotkey;
        set { if (SetField(ref _clipboardHotkey, value)) { Save(); HotkeysChanged?.Invoke(this, EventArgs.Empty); } }
    }

    public Hotkey PanelHotkey
    {
        get => _panelHotkey;
        set { if (SetField(ref _panelHotkey, value)) { Save(); HotkeysChanged?.Invoke(this, EventArgs.Empty); } }
    }

    /// <summary>"Translate screen area" (OCR). Settings files from before this shortcut existed get the default.</summary>
    public Hotkey ScreenHotkey
    {
        get => _screenHotkey;
        set { if (SetField(ref _screenHotkey, value)) { Save(); HotkeysChanged?.Invoke(this, EventArgs.Empty); } }
    }

    public bool AutoTranslate
    {
        get => _autoTranslate;
        set { if (SetField(ref _autoTranslate, value)) Save(); }
    }

    public bool OfflineOnly
    {
        get => _offlineOnly;
        set { if (SetField(ref _offlineOnly, value)) Save(); }
    }

    public AppTheme AppTheme
    {
        get => _appTheme;
        set { if (SetField(ref _appTheme, value)) Save(); }
    }

    public PanelSizeMode PanelSizeMode
    {
        get => _panelSizeMode;
        set { if (SetField(ref _panelSizeMode, value)) Save(); }
    }

    public AppUILanguage AppLanguage
    {
        get => _appLanguage;
        set
        {
            if (SetField(ref _appLanguage, value))
            {
                L10n.Selected = value;
                Save();
            }
        }
    }

    public bool HasCompletedFirstRun
    {
        get => _hasCompletedFirstRun;
        set { if (SetField(ref _hasCompletedFirstRun, value)) Save(); }
    }

    public string SourceCode
    {
        get => _sourceCode;
        set { if (SetField(ref _sourceCode, value)) Save(); }
    }

    public string TargetCode
    {
        get => _targetCode;
        set { if (SetField(ref _targetCode, value)) Save(); }
    }

    public DateTimeOffset? OfflineUpdateLastCheck
    {
        get => _offlineUpdateLastCheck;
        set { if (SetField(ref _offlineUpdateLastCheck, value)) Save(); }
    }

    public List<string> OfflineInstalledSnapshot
    {
        get => _offlineInstalledSnapshot;
        set { if (SetField(ref _offlineInstalledSnapshot, value)) Save(); }
    }

    /// <summary>Not persisted; reflects/drives HKCU Run. Reverts and reports <see cref="LoginItemMessage"/> on failure.</summary>
    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set
        {
            if (_launchAtLogin == value)
            {
                return;
            }
            var previous = _launchAtLogin;
            _launchAtLogin = value;
            OnPropertyChanged();
            try
            {
                _setAutostartEnabled(value);
                LoginItemMessage = null;
            }
            catch (Exception ex)
            {
                _launchAtLogin = previous;
                OnPropertyChanged();
                LoginItemMessage = L10n.Format("error.autostart", ex.Message);
            }
        }
    }

    public string? LoginItemMessage
    {
        get => _loginItemMessage;
        private set => SetField(ref _loginItemMessage, value);
    }

    public string? UpdateMessage
    {
        get => _updateMessage;
        private set => SetField(ref _updateMessage, value);
    }

    public string? UpdateUrl
    {
        get => _updateUrl;
        private set => SetField(ref _updateUrl, value);
    }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set => SetField(ref _isCheckingForUpdates, value);
    }

    /// <summary>True while the hotkey recorder UI is capturing a new shortcut (global hotkeys should be suspended).</summary>
    public bool IsRecordingHotkey
    {
        get => _isRecordingHotkey;
        set
        {
            if (SetField(ref _isRecordingHotkey, value))
            {
                RecordingStateChanged?.Invoke(this, value);
            }
        }
    }

    public string? HotkeyError
    {
        get => _hotkeyError;
        set => SetField(ref _hotkeyError, value);
    }

    public void CompleteFirstRun() => HasCompletedFirstRun = true;

    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates)
        {
            return;
        }
        IsCheckingForUpdates = true;
        UpdateMessage = null;
        UpdateUrl = null;
        try
        {
            var result = await UpdateChecker.CheckAsync().ConfigureAwait(true);
            UpdateMessage = result.Message;
            UpdateUrl = result.DownloadUrl;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>System UI language's app code, unless it's English, in which case Spanish (matches the macOS default heuristic).</summary>
    private static string DefaultTargetCode()
    {
        var systemCode = L10n.SystemCode;
        return systemCode == "en" ? DefaultTargetForEnglishSystem : systemCode;
    }

    private static Hotkey ParseHotkeyOrNone(string display) =>
        Hotkey.TryParse(display, out var hotkey) ? hotkey : Hotkey.None;

    private void ApplyData(PersistedData data)
    {
        _selectionHotkey = data.SelectionHotkey is null ? Hotkey.DefaultSelection : ParseHotkeyOrNone(data.SelectionHotkey);
        _clipboardHotkey = data.ClipboardHotkey is null ? Hotkey.DefaultClipboard : ParseHotkeyOrNone(data.ClipboardHotkey);
        _panelHotkey = data.PanelHotkey is null ? Hotkey.DefaultPanel : ParseHotkeyOrNone(data.PanelHotkey);
        _screenHotkey = data.ScreenHotkey is null ? Hotkey.DefaultScreen : ParseHotkeyOrNone(data.ScreenHotkey);
        _autoTranslate = data.AutoTranslate ?? true;
        _offlineOnly = data.OfflineOnly ?? false;
        _appTheme = AppThemeInfo.Parse(data.AppTheme);
        _panelSizeMode = PanelSizeModeInfo.Parse(data.PanelSizeMode);
        _appLanguage = AppUILanguageInfo.Parse(data.AppLanguage);
        _hasCompletedFirstRun = data.HasCompletedFirstRun ?? false;
        _sourceCode = data.SourceCode ?? "auto";
        _targetCode = data.TargetCode ?? DefaultTargetCode();
        _offlineUpdateLastCheck = data.OfflineUpdateLastCheck;
        _offlineInstalledSnapshot = data.OfflineInstalledSnapshot ?? [];
    }

    private static PersistedData? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PersistedData>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SettingsStore: load failed ({ex.GetType().Name})");
            return null;
        }
    }

    private void Save()
    {
        try
        {
            var data = new PersistedData
            {
                SelectionHotkey = _selectionHotkey.Display,
                ClipboardHotkey = _clipboardHotkey.Display,
                PanelHotkey = _panelHotkey.Display,
                ScreenHotkey = _screenHotkey.Display,
                AutoTranslate = _autoTranslate,
                OfflineOnly = _offlineOnly,
                AppTheme = _appTheme.ToString(),
                PanelSizeMode = _panelSizeMode.ToString(),
                AppLanguage = _appLanguage.ToString(),
                HasCompletedFirstRun = _hasCompletedFirstRun,
                SourceCode = _sourceCode,
                TargetCode = _targetCode,
                OfflineUpdateLastCheck = _offlineUpdateLastCheck,
                OfflineInstalledSnapshot = _offlineInstalledSnapshot,
            };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SettingsStore: save failed ({ex.GetType().Name})");
        }
    }

    private sealed class PersistedData
    {
        public string? SelectionHotkey { get; set; }
        public string? ClipboardHotkey { get; set; }
        public string? PanelHotkey { get; set; }
        public string? ScreenHotkey { get; set; }
        public bool? AutoTranslate { get; set; }
        public bool? OfflineOnly { get; set; }
        public string? AppTheme { get; set; }
        public string? PanelSizeMode { get; set; }
        public string? AppLanguage { get; set; }
        public bool? HasCompletedFirstRun { get; set; }
        public string? SourceCode { get; set; }
        public string? TargetCode { get; set; }
        public DateTimeOffset? OfflineUpdateLastCheck { get; set; }
        public List<string>? OfflineInstalledSnapshot { get; set; }
    }
}

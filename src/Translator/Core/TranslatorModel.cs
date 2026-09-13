using System.Collections.ObjectModel;
using System.ComponentModel;
using Translator.Offline;
using Translator.Platform;

namespace Translator.Core;

public sealed record LanguageOption(string Code, string Title);

/// <summary>
/// Seam over <see cref="OfflineModelManager"/> so <see cref="TranslatorModel"/> can be unit-tested
/// without ONNX Runtime or model downloads.
/// </summary>
public interface IOfflineStatusSource
{
    OfflineLanguageState GetState(string languageCode);

    event EventHandler<string>? StateChanged;

    Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Adapts a real <see cref="OfflineModelManager"/> to <see cref="IOfflineStatusSource"/>.</summary>
public sealed class OfflineModelManagerStatusSource(OfflineModelManager manager) : IOfflineStatusSource
{
    public OfflineLanguageState GetState(string languageCode) => manager.GetState(languageCode);

    public event EventHandler<string>? StateChanged
    {
        add => manager.StateChanged += value;
        remove => manager.StateChanged -= value;
    }

    public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken = default) =>
        manager.CheckForUpdatesAsync(cancellationToken);
}

/// <summary>
/// Port of the macOS TranslatorModel (TranslationService.swift): input/output text, language pair,
/// online (Google) + offline engine orchestration, history, speech. Used from the UI thread; awaits
/// resume on the captured SynchronizationContext, and also work fine without one (tests).
/// </summary>
public sealed class TranslatorModel : ObservableObject
{
    public const int MaxInputChars = 5000;
    public const int HistoryLimit = 10;
    public const int OfflineHeavyThreshold = 600;

    private static readonly TimeSpan AutoTranslateDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan OfflineUpdateCheckInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan OfflineTimeout = TimeSpan.FromSeconds(30);

    private readonly SettingsStore _settings;
    private readonly HistoryStore _historyStore;
    private readonly IOnlineTranslator _online;
    private readonly IOfflineTranslator _offline;
    private readonly IOfflineStatusSource _offlineStatus;
    private readonly ISpeechService _speech;

    private string _inputText = "";
    private string _outputText = "";
    private string _sourceCode;
    private string _targetCode;
    private bool _isTranslating;
    private EngineKind _engine = EngineKind.None;
    private string? _errorMessage;
    private string? _notice;
    private string _offlineStatusText = "";
    private bool _offlineReady;
    private string? _offlineUpdateMessage;
    private IReadOnlyList<string> _offlineUpdateCodes = [];

    private string? _lastDetectedSource;
    private string? _lastTargetCode;
    private CancellationTokenSource? _translationCts;
    private CancellationTokenSource? _autoTranslateCts;

    public event EventHandler? OfflineLanguagesRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? HistoryRequested;

    public TranslatorModel(
        SettingsStore settings,
        HistoryStore historyStore,
        IOnlineTranslator onlineTranslator,
        IOfflineTranslator offlineTranslator,
        IOfflineStatusSource offlineStatus,
        ISpeechService speech,
        OfflineModelManager? offlineModelManager = null)
    {
        _settings = settings;
        _historyStore = historyStore;
        _online = onlineTranslator;
        _offline = offlineTranslator;
        _offlineStatus = offlineStatus;
        _speech = speech;
        OfflineModelManager = offlineModelManager;

        _sourceCode = settings.SourceCode;
        _targetCode = settings.TargetCode;
        History = historyStore.Entries;

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        L10n.LanguageChanged += OnLanguageChanged;

        UpdateOfflineFooter();
    }

    /// <summary>Exposed (nullable) so the UI can open the offline-languages window.</summary>
    public OfflineModelManager? OfflineModelManager { get; }

    /// <summary>The task started by the most recent <see cref="Translate"/> call; test-only synchronization hook.</summary>
    internal Task? CurrentTranslationTask { get; private set; }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetField(ref _inputText, value))
            {
                OnPropertyChanged(nameof(InputLength));
                OnPropertyChanged(nameof(CharCounterText));
                OnPropertyChanged(nameof(IsNearLimit));
                OnPropertyChanged(nameof(IsOverLimit));
                if (_settings.AutoTranslate)
                {
                    ScheduleAutoTranslate();
                }
            }
        }
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetField(ref _outputText, value);
    }

    public string SourceCode
    {
        get => _sourceCode;
        set
        {
            if (SetField(ref _sourceCode, value))
            {
                _settings.SourceCode = value;
                UpdateOfflineFooter();
                Translate();
            }
        }
    }

    public string TargetCode
    {
        get => _targetCode;
        set
        {
            if (SetField(ref _targetCode, value))
            {
                _settings.TargetCode = value;
                UpdateOfflineFooter();
                Translate();
            }
        }
    }

    public bool IsTranslating
    {
        get => _isTranslating;
        private set => SetField(ref _isTranslating, value);
    }

    public EngineKind Engine
    {
        get => _engine;
        private set
        {
            if (SetField(ref _engine, value))
            {
                OnPropertyChanged(nameof(EngineLabel));
                OnPropertyChanged(nameof(EnginePrivacyText));
            }
        }
    }

    public string EngineLabel => Engine switch
    {
        EngineKind.Google => L10n.T("engine.google.label"),
        EngineKind.Offline => L10n.T("engine.offline.label"),
        _ => L10n.T("engine.pending"),
    };

    public string EnginePrivacyText => Engine switch
    {
        EngineKind.Google => L10n.T("engine.google.privacy"),
        EngineKind.Offline => L10n.T("engine.offline.privacy"),
        _ => _settings.OfflineOnly ? L10n.T("engine.offline.only") : L10n.T("engine.pending"),
    };

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string? Notice
    {
        get => _notice;
        private set => SetField(ref _notice, value);
    }

    public string OfflineStatusText
    {
        get => _offlineStatusText;
        private set => SetField(ref _offlineStatusText, value);
    }

    public bool OfflineReady
    {
        get => _offlineReady;
        private set => SetField(ref _offlineReady, value);
    }

    public string? OfflineUpdateMessage
    {
        get => _offlineUpdateMessage;
        private set => SetField(ref _offlineUpdateMessage, value);
    }

    public IReadOnlyList<string> OfflineUpdateCodes
    {
        get => _offlineUpdateCodes;
        private set => SetField(ref _offlineUpdateCodes, value);
    }

    public ObservableCollection<HistoryEntry> History { get; }

    public IReadOnlyList<LanguageOption> SourceOptions =>
        (LanguageOption[])
        [
            new LanguageOption(AppLanguage.AutoCode, L10n.T("auto")),
            .. AppLanguage.All.Select(l => new LanguageOption(l.GoogleCode, L10n.LanguageName(l.GoogleCode))),
        ];

    public IReadOnlyList<LanguageOption> TargetOptions =>
        AppLanguage.All.Select(l => new LanguageOption(l.GoogleCode, L10n.LanguageName(l.GoogleCode))).ToList();

    public int InputLength => _inputText.Length;

    public string CharCounterText => $"{InputLength} / {MaxInputChars}";

    public bool IsNearLimit => InputLength > MaxInputChars * 0.9 && !IsOverLimit;

    public bool IsOverLimit => InputLength > MaxInputChars;

    public string InputPlaceholder => L10n.Format("input.placeholder", _settings.SelectionHotkey.Display);

    public void Translate()
    {
        var text = _inputText.Trim();
        if (text.Length == 0)
        {
            return;
        }
        if (text.Length > MaxInputChars)
        {
            ErrorMessage = L10n.Format("error.too.long", MaxInputChars, text.Length);
            return;
        }

        _translationCts?.Cancel();
        var cts = new CancellationTokenSource();
        _translationCts = cts;
        CurrentTranslationTask = PerformTranslationAsync(text, cts);
    }

    public void SwapLanguages()
    {
        string newSource;
        string newTarget;
        if (_sourceCode == AppLanguage.AutoCode)
        {
            newSource = _targetCode;
            var detected = _lastDetectedSource is not null ? AppLanguage.ByGoogle(_lastDetectedSource)?.GoogleCode : null;
            newTarget = detected ?? DefaultCounterpartCode(_targetCode);
        }
        else
        {
            newSource = _targetCode;
            newTarget = _sourceCode;
        }
        if (newSource == newTarget)
        {
            newTarget = DefaultCounterpartCode(newSource);
        }

        SetPairSilently(newSource, newTarget);
        UpdateOfflineFooter();
        Translate();
    }

    public void SpeakInput() => _ = SpeakAsync(_inputText, ResolveSpeakSourceCode());

    public void SpeakOutput() => _ = SpeakAsync(_outputText, _lastTargetCode ?? _targetCode);

    public void CopyResult()
    {
        if (!string.IsNullOrEmpty(_outputText))
        {
            ClipboardService.SetText(_outputText);
        }
    }

    public void Clear()
    {
        _translationCts?.Cancel();
        _autoTranslateCts?.Cancel();
        InputText = "";
        OutputText = "";
        ErrorMessage = null;
        Notice = null;
        Engine = EngineKind.None;
    }

    public void ClearHistory() => _historyStore.Clear();

    /// <summary>Called before showing a "translate selection" result, mirroring macOS translateSelection.</summary>
    public void PrepareForSelection()
    {
        ErrorMessage = null;
        OutputText = "";
        Engine = EngineKind.None;
    }

    public void ShowError(string message) => ErrorMessage = message;

    public void RequestOfflineLanguages()
    {
        ErrorMessage = null;
        OfflineLanguagesRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void RequestHistory() => HistoryRequested?.Invoke(this, EventArgs.Empty);

    public void DismissOfflineUpdateNotice()
    {
        OfflineUpdateMessage = null;
        OfflineUpdateCodes = [];
    }

    /// <summary>Pair readiness/status footer; source "auto" resolves to a language counterpart to the current target.</summary>
    public void UpdateOfflineFooter()
    {
        var source = _sourceCode == AppLanguage.AutoCode ? DefaultCounterpartCode(_targetCode) : _sourceCode;
        var ready = IsOfflinePairReady(source, _targetCode);
        OfflineReady = ready;
        var sourceName = L10n.LanguageName(source);
        var targetName = L10n.LanguageName(_targetCode);
        OfflineStatusText = ready
            ? L10n.Format("offline.status.ready", sourceName, targetName)
            : L10n.Format("offline.status.missing", sourceName, targetName);
    }

    public async Task RefreshOfflineStatusAsync(bool checkForUpdates = false)
    {
        OfflineStatusText = L10n.T("offline.status.checking");
        if (checkForUpdates)
        {
            try
            {
                var changed = await _offlineStatus.CheckForUpdatesAsync().ConfigureAwait(true);
                _settings.OfflineUpdateLastCheck = DateTimeOffset.Now;
                DetectOfflineUpdates(changed);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"TranslatorModel: offline update check failed ({ex.GetType().Name})");
            }
        }
        UpdateOfflineFooter();
    }

    /// <summary>Checks for offline-model updates at most once a week, tracked via settings.OfflineUpdateLastCheck.</summary>
    public void SchedulePeriodicOfflineUpdateCheck()
    {
        var last = _settings.OfflineUpdateLastCheck;
        if (last is not null && DateTimeOffset.Now - last.Value < OfflineUpdateCheckInterval)
        {
            return;
        }
        _ = RefreshOfflineStatusAsync(checkForUpdates: true);
    }

    private static string DefaultCounterpartCode(string target) => target == "en" ? UiLanguageGoogleCode() : "en";

    private static string UiLanguageGoogleCode()
    {
        var code = L10n.CurrentCode;
        return code == "zh" ? "zh-CN" : code;
    }

    private static string NormalizeForComparison(string code) =>
        string.Equals(code, "zh-CN", StringComparison.OrdinalIgnoreCase) ? "zh" : code.ToLowerInvariant();

    private void SetPairSilently(string sourceCode, string targetCode)
    {
        if (SetField(ref _sourceCode, sourceCode, nameof(SourceCode)))
        {
            _settings.SourceCode = sourceCode;
        }
        if (SetField(ref _targetCode, targetCode, nameof(TargetCode)))
        {
            _settings.TargetCode = targetCode;
        }
    }

    private (string? Source, string Target) ResolvedPair(string text)
    {
        string? source = _sourceCode == AppLanguage.AutoCode ? null : _sourceCode;
        var target = _targetCode;
        if (source is null && target == "ru" && AppLanguage.HasCyrillic(text))
        {
            target = "en";
        }
        return (source, target);
    }

    private async Task PerformTranslationAsync(string text, CancellationTokenSource cts)
    {
        IsTranslating = true;
        ErrorMessage = null;
        Notice = null;

        try
        {
            var (source, target) = ResolvedPair(text);

            if (!_settings.OfflineOnly)
            {
                try
                {
                    var result = await _online.TranslateAsync(text, source ?? AppLanguage.AutoCode, target, cts.Token).ConfigureAwait(true);
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    var (finalResult, finalTarget) = await ResolveSmartPairAsync(text, source, target, result, cts.Token).ConfigureAwait(true);
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    _lastDetectedSource = finalResult.DetectedSource;
                    _lastTargetCode = finalTarget;
                    OutputText = finalResult.Text;
                    Engine = EngineKind.Google;
                    RecordHistory(text, finalResult.Text, source, finalTarget, EngineKind.Google, finalResult.DetectedSource);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }
                    // Google failed (network, HTTP, parsing): fall through to the offline engine.
                }
            }
            else
            {
                Notice = L10n.T("notice.offline.only");
            }

            await TranslateOfflineAsync(text, source, target, cts).ConfigureAwait(true);
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsTranslating = false;
            }
        }
    }

    /// <summary>
    /// If Google detected the source as being the same language as the target (source was "auto"),
    /// retranslate into a different target so the user doesn't get their own text back untranslated.
    /// </summary>
    private async Task<(OnlineTranslation Result, string Target)> ResolveSmartPairAsync(
        string text, string? source, string target, OnlineTranslation result, CancellationToken cancellationToken)
    {
        if (source is not null || result.DetectedSource is null)
        {
            return (result, target);
        }
        if (NormalizeForComparison(result.DetectedSource) != NormalizeForComparison(target))
        {
            return (result, target);
        }

        var newTarget = DefaultCounterpartCode(target);
        if (newTarget == target)
        {
            return (result, target);
        }

        var retried = await _online.TranslateAsync(text, AppLanguage.AutoCode, newTarget, cancellationToken).ConfigureAwait(true);
        return (retried, newTarget);
    }

    private async Task TranslateOfflineAsync(string text, string? source, string target, CancellationTokenSource cts)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        timeoutCts.CancelAfter(OfflineTimeout);

        try
        {
            var result = await _offline.TranslateAsync(text, source, target, timeoutCts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _lastTargetCode = target;
            OutputText = result.Text;
            Engine = EngineKind.Offline;
            if (text.Length > OfflineHeavyThreshold)
            {
                Notice = L10n.T("notice.offline.slow");
            }
            RecordHistory(text, result.Text, source, target, EngineKind.Offline, result.SourceCode);
        }
        catch (OperationCanceledException)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }
            ErrorMessage = L10n.T("error.offline.timeout");
        }
        catch (OfflineTranslationException ex)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }
            var effectiveSource = source ?? DefaultCounterpartCode(target);
            var ready = IsOfflinePairReady(effectiveSource, target);
            if (_settings.OfflineOnly && !ready)
            {
                ErrorMessage = L10n.T("error.offline.only.missing");
            }
            else if (ready)
            {
                ErrorMessage = L10n.Format("error.translate.failed", ex.Message);
            }
            else
            {
                ErrorMessage = L10n.T("error.no.internet");
            }
        }
    }

    /// <summary>A pair is offline-ready when both languages are installed; English is the built-in pivot.</summary>
    private bool IsOfflinePairReady(string first, string second)
    {
        bool Ready(string code) => code == "en" || _offlineStatus.GetState(code) == OfflineLanguageState.Installed;
        return Ready(first) && Ready(second);
    }

    private void RecordHistory(string input, string output, string? source, string target, EngineKind engine, string? detectedSource)
    {
        _historyStore.Add(new HistoryEntry
        {
            SourceCode = source,
            DetectedSourceCode = source is null ? detectedSource : null,
            TargetCode = target,
            Engine = engine,
            Input = input,
            Output = output,
        });
    }

    private string ResolveSpeakSourceCode()
    {
        if (_sourceCode != AppLanguage.AutoCode)
        {
            return _sourceCode;
        }
        if (_lastDetectedSource is not null && AppLanguage.ByGoogle(_lastDetectedSource) is not null)
        {
            return _lastDetectedSource;
        }
        return AppLanguage.HasCyrillic(_inputText) ? "ru" : "en";
    }

    private async Task SpeakAsync(string text, string code)
    {
        var error = await _speech.SpeakAsync(text, code).ConfigureAwait(true);
        if (error is not null)
        {
            ErrorMessage = error;
        }
    }

    private void DetectOfflineUpdates(IReadOnlyList<string> changedFromCheck)
    {
        var previouslyInstalled = _settings.OfflineInstalledSnapshot;
        var currentInstalled = Translator.Offline.OfflineModelManager.SupportedLanguages
            .Where(code => _offlineStatus.GetState(code) == OfflineLanguageState.Installed)
            .ToList();

        var disappeared = previouslyInstalled.Where(code => !currentInstalled.Contains(code));
        var codes = changedFromCheck.Concat(disappeared).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();

        _settings.OfflineInstalledSnapshot = currentInstalled;

        if (codes.Count == 0)
        {
            OfflineUpdateMessage = null;
            OfflineUpdateCodes = [];
            return;
        }

        OfflineUpdateCodes = codes;
        OfflineUpdateMessage = L10n.Format("offline.update.message", string.Join(", ", codes.Select(L10n.LanguageName)));
    }

    private void ScheduleAutoTranslate()
    {
        _autoTranslateCts?.Cancel();
        var cts = new CancellationTokenSource();
        _autoTranslateCts = cts;
        _ = RunAutoTranslateAsync(cts);
    }

    private async Task RunAutoTranslateAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(AutoTranslateDelay, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!cts.IsCancellationRequested)
        {
            Translate();
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsStore.OfflineOnly):
                OnPropertyChanged(nameof(EnginePrivacyText));
                UpdateOfflineFooter();
                Translate();
                break;
            case nameof(SettingsStore.SelectionHotkey):
                OnPropertyChanged(nameof(InputPlaceholder));
                break;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EngineLabel));
        OnPropertyChanged(nameof(EnginePrivacyText));
        OnPropertyChanged(nameof(InputPlaceholder));
        OnPropertyChanged(nameof(SourceOptions));
        OnPropertyChanged(nameof(TargetOptions));
        UpdateOfflineFooter();
    }
}

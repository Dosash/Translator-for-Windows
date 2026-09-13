using Translator.Core;
using Translator.Offline;

namespace Translator.UI.Windows;

public enum OfflineDot
{
    Ready,
    Missing,
    Unavailable,
}

/// <summary>What a language row in the offline-languages window shows. Pure, unit-tested.</summary>
public readonly record struct OfflineRowActions(
    bool ShowDownload,
    bool ShowUpdate,
    bool ShowRemove,
    bool ShowDownloaded,
    bool ShowUnsupported,
    bool ShowProgress,
    OfflineDot Dot)
{
    /// <param name="state">Manager state of the language.</param>
    /// <param name="downloading">This window is downloading the language right now.</param>
    /// <param name="updateFlagged">The periodic check listed the language in <see cref="TranslatorModel.OfflineUpdateCodes"/>.</param>
    public static OfflineRowActions For(OfflineLanguageState state, bool downloading, bool updateFlagged)
    {
        if (downloading || state == OfflineLanguageState.Downloading)
        {
            return new(false, false, false, false, false, true, OfflineDot.Missing);
        }
        return state switch
        {
            OfflineLanguageState.Installed when updateFlagged => new(false, true, true, false, false, false, OfflineDot.Ready),
            OfflineLanguageState.Installed => new(false, false, true, true, false, false, OfflineDot.Ready),
            OfflineLanguageState.UpdateAvailable => new(false, true, true, false, false, false, OfflineDot.Ready),
            OfflineLanguageState.Unsupported => new(false, false, false, false, true, false, OfflineDot.Unavailable),
            _ when updateFlagged => new(false, true, false, false, false, false, OfflineDot.Missing),
            _ => new(true, false, false, false, false, false, OfflineDot.Missing),
        };
    }
}

/// <summary>Row view model for one downloadable offline language.</summary>
public sealed class OfflineLanguageRow : ObservableObject
{
    private OfflineLanguageState _state;
    private bool _isDownloading;
    private bool _isAnyBusy;
    private bool _updateFlagged;
    private double _progress;
    private long _sizeBytes;

    public OfflineLanguageRow(string code) => Code = code;

    public string Code { get; }

    public string Name => L10n.LanguageName(Code);

    public bool IsBasicQuality => OfflineModelManager.IsBasicQuality(Code);

    public OfflineLanguageState State
    {
        get => _state;
        set { if (SetField(ref _state, value)) RaiseActions(); }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set { if (SetField(ref _isDownloading, value)) RaiseActions(); }
    }

    /// <summary>Another row is downloading; one download at a time, like the macOS window.</summary>
    public bool IsAnyBusy
    {
        get => _isAnyBusy;
        set { if (SetField(ref _isAnyBusy, value)) OnPropertyChanged(nameof(CanStart)); }
    }

    public bool UpdateFlagged
    {
        get => _updateFlagged;
        set { if (SetField(ref _updateFlagged, value)) RaiseActions(); }
    }

    /// <summary>0–100.</summary>
    public double Progress
    {
        get => _progress;
        set { if (SetField(ref _progress, value)) OnPropertyChanged(nameof(ProgressText)); }
    }

    public long SizeBytes
    {
        get => _sizeBytes;
        set { if (SetField(ref _sizeBytes, value)) OnPropertyChanged(nameof(SizeText)); }
    }

    public OfflineRowActions Actions => OfflineRowActions.For(State, IsDownloading, UpdateFlagged);

    public bool CanStart => !IsAnyBusy;

    public string ProgressText => L10n.Format("offline.downloading", (int)Math.Round(Progress));

    public string SizeText => SizeBytes > 0 ? L10n.Format("offline.size", (int)Math.Round(SizeBytes / 1_000_000.0)) : string.Empty;

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SizeText));
    }

    private void RaiseActions() => OnPropertyChanged(nameof(Actions));
}

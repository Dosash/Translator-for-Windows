namespace Translator.Offline;

/// <summary>
/// Downloads, tracks and runs offline translation models (one language = a pair of
/// models "xx→en" and "en→xx"; English is the pivot). Thread-safe.
/// </summary>
/// <remarks>Stub: the real implementation replaces the bodies, keeping this public surface.</remarks>
public sealed class OfflineModelManager : IOfflineTranslator, IDisposable
{
    public const string BaseLanguage = "en";

    public OfflineModelManager(string modelsDirectory, HttpClient? httpClient = null)
    {
        ModelsDirectory = modelsDirectory;
    }

    public string ModelsDirectory { get; }

    /// <summary>Languages that can be downloaded (every app language except English).</summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } =
        ["ru", "es", "de", "fr", "it", "pt", "zh-CN", "ja", "ko", "tr", "uk"];

    /// <summary>Raised (on a background thread) with the language code whose state changed.</summary>
    public event EventHandler<string>? StateChanged;

    public OfflineLanguageState GetState(string languageCode) =>
        languageCode == BaseLanguage ? OfflineLanguageState.Installed : OfflineLanguageState.NotInstalled;

    /// <summary>True when the models for this language are known to be of basic quality.</summary>
    public static bool IsBasicQuality(string languageCode) => false;

    public long GetDownloadSizeBytes(string languageCode) => 0;

    public long GetInstalledSizeBytes(string languageCode) => 0;

    public Task DownloadAsync(
        string languageCode,
        IProgress<OfflineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task RemoveAsync(string languageCode, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Returns installed languages whose remote model files changed since download.</summary>
    public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public bool IsPairReady(string sourceCode, string targetCode) => false;

    public Task<OfflineTranslation> TranslateAsync(
        string text,
        string? sourceCode,
        string targetCode,
        CancellationToken cancellationToken = default) =>
        throw new OfflineTranslationException("Offline translation is not available yet.");

    /// <summary>Best-effort language detection for offline mode; null when unsure.</summary>
    public static string? DetectLanguage(string text) => null;

    public void Dispose()
    {
    }
}

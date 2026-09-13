namespace Translator.Offline;

/// <summary>
/// On-device translation. Language codes are the app's Google-style codes:
/// ru, en, es, de, fr, it, pt, zh-CN, ja, ko, tr, uk.
/// </summary>
public interface IOfflineTranslator
{
    /// <summary>True when both languages of the pair are installed (English is built in as the pivot).</summary>
    bool IsPairReady(string sourceCode, string targetCode);

    /// <summary>
    /// Translates <paramref name="text"/>. <paramref name="sourceCode"/> = null means "detect".
    /// Throws <see cref="OfflineTranslationException"/> when the pair is not installed or inference fails.
    /// </summary>
    Task<OfflineTranslation> TranslateAsync(
        string text,
        string? sourceCode,
        string targetCode,
        CancellationToken cancellationToken = default);
}

public sealed record OfflineTranslation(string Text, string SourceCode);

public enum OfflineLanguageState
{
    NotInstalled,
    Downloading,
    Installed,
    UpdateAvailable,
    Unsupported,
}

public sealed record OfflineDownloadProgress(string LanguageCode, long BytesReceived, long? TotalBytes);

public sealed class OfflineTranslationException : Exception
{
    public OfflineTranslationException(string message, Exception? inner = null) : base(message, inner) { }
}

namespace Translator.Core;

/// <summary>A translation language. <see cref="GoogleCode"/> is the app-wide language id.</summary>
public sealed record AppLanguage(string GoogleCode, string SpeechTag)
{
    public static IReadOnlyList<AppLanguage> All { get; } =
    [
        new("ru", "ru-RU"),
        new("en", "en-US"),
        new("es", "es-ES"),
        new("de", "de-DE"),
        new("fr", "fr-FR"),
        new("it", "it-IT"),
        new("pt", "pt-BR"),
        new("zh-CN", "zh-CN"),
        new("ja", "ja-JP"),
        new("ko", "ko-KR"),
        new("tr", "tr-TR"),
        new("uk", "uk-UA"),
    ];

    public const string AutoCode = "auto";

    public static AppLanguage? ByGoogle(string? code) =>
        All.FirstOrDefault(language => string.Equals(language.GoogleCode, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Contains Cyrillic letters (used for the smart ru/en direction).</summary>
    public static bool HasCyrillic(string text) => text.Any(c => c is >= 'Ѐ' and <= 'ӿ');
}

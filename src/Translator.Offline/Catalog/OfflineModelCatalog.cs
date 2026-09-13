namespace Translator.Offline;

/// <summary>A file published in a Hugging Face repository.</summary>
public sealed record OfflineModelFile(string Repo, string Path);

/// <summary>
/// One translation direction: an Opus-MT (Marian) model exported to ONNX.
/// <paramref name="TokenizerRepo"/> is set when the export repo lacks the SentencePiece files.
/// </summary>
public sealed record OfflineModelInfo(
    string Id,
    string Repo,
    string EncoderPath,
    string DecoderPath,
    long EstimatedSizeBytes,
    string? TokenizerRepo = null,
    bool SourceIdsFromSentencePiece = false)
{
    public const string SourceSpm = "source.spm";
    public const string TargetSpm = "target.spm";
    public const string VocabFile = "vocab.json";
    public const string ConfigFile = "config.json";
    public const string GenerationConfigFile = "generation_config.json";
    public const string TokenizerConfigFile = "tokenizer_config.json";

    /// <summary>Only the files needed for inference (repos contain many unused ONNX variants).</summary>
    public IReadOnlyList<OfflineModelFile> Files
    {
        get
        {
            var tokenizerRepo = TokenizerRepo ?? Repo;
            return
            [
                new(Repo, EncoderPath),
                new(Repo, DecoderPath),
                new(Repo, ConfigFile),
                new(Repo, GenerationConfigFile),
                new(tokenizerRepo, SourceSpm),
                new(tokenizerRepo, TargetSpm),
                new(tokenizerRepo, VocabFile),
                new(tokenizerRepo, TokenizerConfigFile),
            ];
        }
    }

    public IReadOnlyList<string> Repos => TokenizerRepo is null || TokenizerRepo == Repo ? [Repo] : [Repo, TokenizerRepo];
}

/// <summary>
/// Models for one language. English is the pivot, so each language needs "xx→en" and "en→xx".
/// Multilingual models need <see cref="FromEnglishTargetToken"/> (e.g. "&gt;&gt;tur&lt;&lt;") prepended to the source.
/// </summary>
public sealed record OfflineLanguageInfo(
    string Code,
    string ToEnglishModelId,
    string FromEnglishModelId,
    string? FromEnglishTargetToken,
    bool IsBasicQuality)
{
    public IReadOnlyList<string> ModelIds => [ToEnglishModelId, FromEnglishModelId];
}

public static class OfflineModelCatalog
{
    public const string BaseLanguage = "en";

    private const string EncoderQuantized = "onnx/encoder_model_quantized.onnx";
    private const string DecoderMergedQuantized = "onnx/decoder_model_merged_quantized.onnx";

    public static IReadOnlyList<OfflineModelInfo> Models { get; } =
    [
        Xenova("opus-mt-ru-en", 109.8),
        Xenova("opus-mt-en-ru", 109.8),
        Xenova("opus-mt-es-en", 111.1),
        Xenova("opus-mt-en-es", 111.1),
        Xenova("opus-mt-de-en", 103.9),
        Xenova("opus-mt-en-de", 103.9),
        Xenova("opus-mt-fr-en", 105.4),
        Xenova("opus-mt-en-fr", 105.4),
        Xenova("opus-mt-it-en", 126.9),
        Xenova("opus-mt-en-it", 126.5),
        Xenova("opus-mt-ROMANCE-en", 110.9),
        Xenova("opus-mt-en-ROMANCE", 110.9),
        Xenova("opus-mt-zh-en", 111.1),
        Xenova("opus-mt-en-zh", 111.1),
        Xenova("opus-mt-ja-en", 106.7),
        Xenova("opus-mt-ko-en", 111.2),
        Xenova("opus-mt-tr-en", 108.5),
        Xenova("opus-mt-uk-en", 108.6),
        Xenova("opus-mt-en-uk", 108.6),
        Xenova("opus-mt-en-mul", 109.9),
        // Opus-MT has no en→ko model in the Xenova set and en-mul has no Korean target;
        // this is the ONNX export of Helsinki-NLP/opus-mt-tc-big-en-ko (tokenizer files live in the original repo).
        // It is a separate-vocabulary model whose vocab.json covers only the target side, so encoder ids
        // come from source.spm (HF MarianTokenizer maps most English pieces to <unk> for it).
        new(
            "opus-mt-tc-big-en-ko",
            "noticemkjung/opus-mt-tc-big-en-ko-ONNX",
            "onnx/encoder_model_quantized.onnx",
            "onnx/decoder_model_merged_q4.onnx",
            Mb(316.6),
            TokenizerRepo: "Helsinki-NLP/opus-mt-tc-big-en-ko",
            SourceIdsFromSentencePiece: true),
    ];

    public static IReadOnlyList<OfflineLanguageInfo> Languages { get; } =
    [
        new("ru", "opus-mt-ru-en", "opus-mt-en-ru", null, false),
        new("es", "opus-mt-es-en", "opus-mt-en-es", null, false),
        new("de", "opus-mt-de-en", "opus-mt-en-de", null, false),
        new("fr", "opus-mt-fr-en", "opus-mt-en-fr", null, false),
        new("it", "opus-mt-it-en", "opus-mt-en-it", null, false),
        // ">>pt<<" mixes European and Brazilian forms; ">>pt_BR<<" is consistent (and matches the pt-BR voice).
        new("pt", "opus-mt-ROMANCE-en", "opus-mt-en-ROMANCE", ">>pt_BR<<", false),
        new("zh-CN", "opus-mt-zh-en", "opus-mt-en-zh", ">>cmn_Hans<<", false),
        // en-jap is trained on Bible text and garbles modern sentences; en-mul is clearly better.
        new("ja", "opus-mt-ja-en", "opus-mt-en-mul", ">>jpn<<", true),
        new("ko", "opus-mt-ko-en", "opus-mt-tc-big-en-ko", null, false),
        new("tr", "opus-mt-tr-en", "opus-mt-en-mul", ">>tur<<", true),
        new("uk", "opus-mt-uk-en", "opus-mt-en-uk", null, false),
    ];

    private static readonly Dictionary<string, OfflineModelInfo> ModelsById =
        Models.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, OfflineLanguageInfo> LanguagesByCode =
        Languages.ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);

    public static OfflineLanguageInfo? FindLanguage(string? code) =>
        code is not null && LanguagesByCode.TryGetValue(NormalizeCode(code), out var info) ? info : null;

    public static OfflineModelInfo? FindModel(string id) => ModelsById.GetValueOrDefault(id);

    public static OfflineModelInfo GetModel(string id) =>
        FindModel(id) ?? throw new KeyError($"Unknown offline model '{id}'.");

    /// <summary>Languages whose translation needs the given model (shared multilingual models have several).</summary>
    public static IReadOnlyList<string> LanguagesUsingModel(string modelId) =>
        Languages.Where(l => l.ModelIds.Contains(modelId, StringComparer.OrdinalIgnoreCase)).Select(l => l.Code).ToList();

    public static bool IsSupported(string? code) =>
        code is not null && (NormalizeCode(code) == BaseLanguage || LanguagesByCode.ContainsKey(NormalizeCode(code)));

    /// <summary>Maps loose spellings ("ZH", "zh-cn", "zh-Hans") to the app's codes.</summary>
    public static string NormalizeCode(string code)
    {
        var trimmed = code.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" or "zh_cn" => "zh-CN",
            var lower => lower,
        };
    }

    private static OfflineModelInfo Xenova(string id, double megabytes) =>
        new(id, "Xenova/" + id, EncoderQuantized, DecoderMergedQuantized, Mb(megabytes));

    private static long Mb(double megabytes) => (long)(megabytes * 1024 * 1024);

    private sealed class KeyError(string message) : KeyNotFoundException(message);
}

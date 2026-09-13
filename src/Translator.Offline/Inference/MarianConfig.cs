using System.Text.Json;

namespace Translator.Offline;

/// <summary>Generation settings from config.json, overridden by generation_config.json when present.</summary>
internal sealed record MarianConfig(
    int DecoderStartTokenId,
    int EosTokenId,
    int PadTokenId,
    int MaxLength,
    int DecoderAttentionHeads,
    int DModel,
    IReadOnlyList<int> SuppressedTokenIds)
{
    public const int DefaultMaxLength = 512;

    public static MarianConfig Load(string directory)
    {
        using var config = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, OfflineModelInfo.ConfigFile)));
        var generationPath = Path.Combine(directory, OfflineModelInfo.GenerationConfigFile);
        using var generation = File.Exists(generationPath) ? JsonDocument.Parse(File.ReadAllBytes(generationPath)) : null;
        return Parse(config.RootElement, generation?.RootElement);
    }

    internal static MarianConfig Parse(JsonElement config, JsonElement? generation)
    {
        JsonElement? Find(string name)
        {
            if (generation is { } g && g.TryGetProperty(name, out var fromGeneration) && fromGeneration.ValueKind != JsonValueKind.Null)
            {
                return fromGeneration;
            }

            return config.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
        }

        int Int(string name, int? fallback = null) =>
            Find(name) is { ValueKind: JsonValueKind.Number } value
                ? value.GetInt32()
                : fallback ?? throw new InvalidDataException($"config.json has no {name}.");

        var pad = Int("pad_token_id");
        var suppressed = new HashSet<int> { pad };
        if (Find("bad_words_ids") is { ValueKind: JsonValueKind.Array } badWords)
        {
            foreach (var sequence in badWords.EnumerateArray())
            {
                if (sequence.ValueKind == JsonValueKind.Array && sequence.GetArrayLength() == 1)
                {
                    suppressed.Add(sequence[0].GetInt32());
                }
            }
        }

        return new MarianConfig(
            DecoderStartTokenId: Int("decoder_start_token_id", pad),
            EosTokenId: Int("eos_token_id"),
            PadTokenId: pad,
            MaxLength: Int("max_length", DefaultMaxLength),
            DecoderAttentionHeads: Int("decoder_attention_heads"),
            DModel: Int("d_model"),
            SuppressedTokenIds: [.. suppressed]);
    }
}

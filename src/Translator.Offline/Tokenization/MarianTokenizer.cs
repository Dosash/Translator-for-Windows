using System.Text;
using System.Text.Json;

namespace Translator.Offline;

/// <summary>
/// Reproduces Hugging Face <c>MarianTokenizer</c> (transformers 5.x, no sacremoses): special tokens are split out
/// first, each remaining chunk goes through the source SentencePiece model, pieces map to ids through vocab.json
/// (Marian ids are not SentencePiece ids; missing pieces become &lt;unk&gt;), and &lt;/s&gt; is appended.
/// </summary>
internal sealed class MarianTokenizer
{
    public const string EosToken = "</s>";
    public const string UnknownToken = "<unk>";
    public const string PadToken = "<pad>";

    private static readonly string[] SpecialTokens = [EosToken, UnknownToken, PadToken];

    private readonly SentencePieceNormalizer _normalizer;
    private readonly UnigramModel _unigram;
    private readonly Dictionary<string, int> _vocabulary;
    private readonly string?[] _tokensById;
    private readonly Dictionary<string, int> _sourceIds;
    private readonly int _sourceUnknownId;
    private readonly int _sourceEosId;

    /// <param name="sourceIdsFromSentencePiece">
    /// For separate-vocabulary exports whose vocab.json only covers the target side: encoder ids are the
    /// source.spm piece ids, as in the original Marian model.
    /// </param>
    public MarianTokenizer(SentencePieceModel sourceModel, Dictionary<string, int> vocabulary, bool sourceIdsFromSentencePiece = false)
    {
        _normalizer = new SentencePieceNormalizer(sourceModel);
        _unigram = new UnigramModel(sourceModel);
        _vocabulary = vocabulary;
        EosId = Require(EosToken);
        UnknownId = Require(UnknownToken);
        PadId = vocabulary.TryGetValue(PadToken, out var pad) ? pad : -1;

        if (sourceIdsFromSentencePiece)
        {
            _sourceIds = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var id = 0; id < sourceModel.Pieces.Length; id++)
            {
                _sourceIds.TryAdd(sourceModel.Pieces[id], id);
            }

            _sourceUnknownId = _unigram.UnknownId;
            _sourceEosId = _sourceIds.GetValueOrDefault(EosToken, EosId);
        }
        else
        {
            _sourceIds = vocabulary;
            _sourceUnknownId = UnknownId;
            _sourceEosId = EosId;
        }

        _tokensById = new string?[vocabulary.Values.Max() + 1];
        foreach (var (token, id) in vocabulary)
        {
            if (id >= 0)
            {
                _tokensById[id] = token;
            }
        }
    }

    public int EosId { get; }

    public int UnknownId { get; }

    public int PadId { get; }

    public static MarianTokenizer Load(string directory, bool sourceIdsFromSentencePiece = false)
    {
        using (var config = TryParse(Path.Combine(directory, OfflineModelInfo.TokenizerConfigFile)))
        {
            if (config is not null
                && config.RootElement.TryGetProperty("separate_vocabs", out var separate)
                && separate.ValueKind == JsonValueKind.True)
            {
                throw new NotSupportedException("Marian models with separate vocabularies are not supported.");
            }
        }

        var model = SentencePieceModel.Load(Path.Combine(directory, OfflineModelInfo.SourceSpm));
        using var stream = File.OpenRead(Path.Combine(directory, OfflineModelInfo.VocabFile));
        var vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(stream)
            ?? throw new InvalidDataException("vocab.json is empty.");
        return new MarianTokenizer(model, vocabulary, sourceIdsFromSentencePiece);
    }

    public bool TryGetId(string token, out int id) => _vocabulary.TryGetValue(token, out id);

    public List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var start = 0;
        while (true)
        {
            var (index, special) = FindSpecialToken(text, start);
            var end = index < 0 ? text.Length : index;
            if (end > start)
            {
                TokenizeChunk(text[start..end], tokens);
            }

            if (special is null)
            {
                return tokens;
            }

            tokens.Add(special);
            start = index + special.Length;
        }
    }

    /// <summary>Model input ids: optional target-language token, pieces, &lt;/s&gt;.</summary>
    public int[] Encode(string text, string? targetToken = null)
    {
        var tokens = Tokenize(text);
        var ids = new int[tokens.Count + 1 + (targetToken is null ? 0 : 1)];
        var index = 0;
        if (targetToken is not null)
        {
            ids[index++] = TryGetId(targetToken, out var tokenId)
                ? tokenId
                : throw new InvalidOperationException($"Target language token {targetToken} is not in the model vocabulary.");
        }

        foreach (var token in tokens)
        {
            ids[index++] = _sourceIds.TryGetValue(token, out var id) ? id : _sourceUnknownId;
        }

        ids[index] = _sourceEosId;
        return ids;
    }

    public int CountTokens(string text) => Tokenize(text).Count + 1;

    public string Decode(IEnumerable<int> ids)
    {
        var builder = new StringBuilder();
        foreach (var id in ids)
        {
            if (id == EosId || id == PadId || id == UnknownId || (uint)id >= (uint)_tokensById.Length)
            {
                continue;
            }

            if (_tokensById[id] is not { } token || (token.StartsWith(">>", StringComparison.Ordinal) && token.EndsWith("<<", StringComparison.Ordinal)))
            {
                continue;
            }

            builder.Append(token);
        }

        return builder.Replace('▁', ' ').ToString().Trim();
    }

    private void TokenizeChunk(string chunk, List<string> tokens)
    {
        // MarianTokenizer._tokenize peels a leading ">>code<<" off each chunk before SentencePiece.
        if (chunk.StartsWith(">>", StringComparison.Ordinal))
        {
            var close = chunk.IndexOf("<<", StringComparison.Ordinal);
            if (close >= 0)
            {
                tokens.Add(chunk[..(close + 2)]);
                chunk = chunk[(close + 2)..];
            }
        }

        _unigram.Encode(_normalizer.Normalize(chunk), tokens);
    }

    private static (int Index, string? Token) FindSpecialToken(string text, int start)
    {
        var bestIndex = -1;
        string? best = null;
        foreach (var token in SpecialTokens)
        {
            var index = text.IndexOf(token, start, StringComparison.Ordinal);
            if (index >= 0 && (bestIndex < 0 || index < bestIndex || (index == bestIndex && token.Length > best!.Length)))
            {
                bestIndex = index;
                best = token;
            }
        }

        return (bestIndex, best);
    }

    private int Require(string token) =>
        _vocabulary.TryGetValue(token, out var id) ? id : throw new InvalidDataException($"vocab.json has no {token} token.");

    private static JsonDocument? TryParse(string path) =>
        File.Exists(path) ? JsonDocument.Parse(File.ReadAllBytes(path)) : null;
}

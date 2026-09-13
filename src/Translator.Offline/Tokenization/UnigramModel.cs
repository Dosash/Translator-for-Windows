using System.Text;

namespace Translator.Offline;

/// <summary>
/// Port of sentencepiece's unigram <c>EncodeOptimized</c> Viterbi search. Candidate order, tie-breaking and
/// the float/double mix of the C++ scoring are kept so segmentations match the reference exactly.
/// </summary>
internal sealed class UnigramModel
{
    private const float UnknownPenalty = 10f;
    private const float FloatMin = 1.17549435E-38f; // C++ FLT_MIN, the initial max_score_

    private readonly SentencePieceType[] _types;
    private readonly float[] _scores;
    private readonly Dictionary<long, int> _edges = new();
    private readonly List<int> _nodePiece = [-1];
    private readonly float _minScore = float.MaxValue;
    private readonly float _maxScore = FloatMin;

    public UnigramModel(SentencePieceModel model)
    {
        if (model.ModelType != SentencePieceModel.UnigramModelType)
        {
            throw new NotSupportedException($"SentencePiece model type {model.ModelType} is not supported (unigram only).");
        }

        if (model.ByteFallback)
        {
            throw new NotSupportedException("SentencePiece byte fallback is not supported.");
        }

        _types = model.Types;
        _scores = model.Scores;
        UnknownId = -1;
        for (var id = 0; id < model.Pieces.Length; id++)
        {
            switch (_types[id])
            {
                case SentencePieceType.Normal:
                    _minScore = Math.Min(_minScore, _scores[id]);
                    _maxScore = Math.Max(_maxScore, _scores[id]);
                    Insert(model.Pieces[id], id);
                    break;
                case SentencePieceType.UserDefined or SentencePieceType.Unused:
                    Insert(model.Pieces[id], id);
                    break;
                case SentencePieceType.Unknown:
                    UnknownId = id;
                    break;
            }
        }

        if (UnknownId < 0)
        {
            throw new InvalidDataException("SentencePiece model has no <unk> piece.");
        }
    }

    public int UnknownId { get; }

    /// <summary>Appends the pieces of an already normalized string; runs of unknown characters become one piece.</summary>
    public void Encode(string normalized, List<string> pieces)
    {
        var length = normalized.Length;
        if (length == 0)
        {
            return;
        }

        var bestScores = new float[length + 1];
        var starts = new int[length + 1];
        var ids = new int[length + 1];
        Array.Fill(starts, -1);
        var unknownScore = _minScore - UnknownPenalty;

        var position = 0;
        while (position < length)
        {
            var scoreTillHere = bestScores[position];
            var characterLength = char.IsHighSurrogate(normalized[position]) && position + 1 < length && char.IsLowSurrogate(normalized[position + 1]) ? 2 : 1;
            var hasSingleCharacterPiece = false;
            var node = 0;
            for (var k = position; k < length; k++)
            {
                if (!_edges.TryGetValue(EdgeKey(node, normalized[k]), out node))
                {
                    break;
                }

                var id = _nodePiece[node];
                if (id < 0 || _types[id] == SentencePieceType.Unused)
                {
                    continue;
                }

                var end = k + 1;
                // In C++ the conditional mixes double and float, so the candidate is computed in double.
                var score = _types[id] == SentencePieceType.UserDefined
                    ? Encoding.UTF8.GetByteCount(normalized.AsSpan(position, end - position)) * _maxScore - 0.1
                    : _scores[id];
                var candidate = score + scoreTillHere;
                if (starts[end] == -1 || candidate > bestScores[end])
                {
                    bestScores[end] = (float)candidate;
                    starts[end] = position;
                    ids[end] = id;
                }

                if (!hasSingleCharacterPiece && end - position == characterLength)
                {
                    hasSingleCharacterPiece = true;
                }
            }

            if (!hasSingleCharacterPiece)
            {
                var end = position + characterLength;
                var candidate = unknownScore + scoreTillHere;
                if (starts[end] == -1 || candidate > bestScores[end])
                {
                    bestScores[end] = candidate;
                    starts[end] = position;
                    ids[end] = UnknownId;
                }
            }

            position += characterLength;
        }

        var path = new List<(int Start, int End, int Id)>();
        for (var end = length; end > 0; end = starts[end])
        {
            path.Add((starts[end], end, ids[end]));
        }

        var previousUnknown = false;
        for (var i = path.Count - 1; i >= 0; i--)
        {
            var (start, end, id) = path[i];
            var piece = normalized[start..end];
            var isUnknown = id == UnknownId;
            if (isUnknown && previousUnknown)
            {
                pieces[^1] += piece;
            }
            else
            {
                pieces.Add(piece);
            }

            previousUnknown = isUnknown;
        }
    }

    private void Insert(string piece, int id)
    {
        var node = 0;
        foreach (var c in piece)
        {
            var key = EdgeKey(node, c);
            if (!_edges.TryGetValue(key, out var child))
            {
                child = _nodePiece.Count;
                _nodePiece.Add(-1);
                _edges[key] = child;
            }

            node = child;
        }

        if (node != 0 && _nodePiece[node] < 0)
        {
            _nodePiece[node] = id;
        }
    }

    private static long EdgeKey(int node, char c) => ((long)node << 16) | c;
}

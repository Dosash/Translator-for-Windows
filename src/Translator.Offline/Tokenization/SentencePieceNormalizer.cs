using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Translator.Offline;

/// <summary>
/// Port of sentencepiece's <c>Normalizer::Normalize</c>: longest-prefix rewriting through the precompiled
/// charsmap (a darts-clone double-array trie), whitespace folding and the U+2581 marker. It works on UTF-8
/// bytes like the C++ code, because rules match byte sequences, not characters.
/// </summary>
internal sealed class SentencePieceNormalizer
{
    private static ReadOnlySpan<byte> SpaceSymbol => [0xE2, 0x96, 0x81];
    private static ReadOnlySpan<byte> ReplacementCharacter => [0xEF, 0xBF, 0xBD];

    private readonly uint[]? _trie;
    private readonly byte[] _replacements = [];
    private readonly byte[][] _userDefinedSymbols;
    private readonly bool _addDummyPrefix;
    private readonly bool _removeExtraWhitespaces;
    private readonly bool _escapeWhitespaces;
    private readonly bool _treatWhitespaceAsSuffix;

    public SentencePieceNormalizer(SentencePieceModel model)
    {
        _addDummyPrefix = model.AddDummyPrefix;
        _removeExtraWhitespaces = model.RemoveExtraWhitespaces;
        _escapeWhitespaces = model.EscapeWhitespaces;
        _treatWhitespaceAsSuffix = model.TreatWhitespaceAsSuffix;
        _userDefinedSymbols = model.Pieces
            .Where((_, i) => model.Types[i] == SentencePieceType.UserDefined)
            .Select(Encoding.UTF8.GetBytes)
            .OrderByDescending(bytes => bytes.Length)
            .ToArray();

        if (model.PrecompiledCharsmap is { Length: > 4 } blob)
        {
            var trieSize = BinaryPrimitives.ReadUInt32LittleEndian(blob);
            if (trieSize >= blob.Length - 4 || trieSize % 4 != 0)
            {
                throw new InvalidDataException("Broken precompiled charsmap in SentencePiece model.");
            }

            _trie = new uint[trieSize / 4];
            for (var i = 0; i < _trie.Length; i++)
            {
                _trie[i] = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(4 + i * 4));
            }

            _replacements = blob[(4 + (int)trieSize)..];
        }
    }

    public string Normalize(string text)
    {
        if (text.Length == 0)
        {
            return "";
        }

        var input = Encoding.UTF8.GetBytes(text);
        var output = new ArrayBufferWriter<byte>(input.Length * 3 + 3);
        var position = 0;

        if (_removeExtraWhitespaces)
        {
            while (position < input.Length)
            {
                var consumed = NormalizePrefix(input, position, out var piece);
                if (piece.Length != 1 || piece[0] != (byte)' ')
                {
                    break;
                }

                position += consumed;
            }
        }

        if (position >= input.Length)
        {
            return "";
        }

        if (!_treatWhitespaceAsSuffix && _addDummyPrefix)
        {
            AppendSpace(output);
        }

        var isPreviousSpace = _removeExtraWhitespaces;
        while (position < input.Length)
        {
            var consumed = NormalizePrefix(input, position, out var piece);
            if (isPreviousSpace)
            {
                while (piece.Length > 0 && piece[0] == (byte)' ')
                {
                    piece = piece[1..];
                }
            }

            if (piece.Length > 0)
            {
                foreach (var b in piece)
                {
                    if (_escapeWhitespaces && b == (byte)' ')
                    {
                        output.Write(SpaceSymbol);
                    }
                    else
                    {
                        output.Write([b]);
                    }
                }

                isPreviousSpace = piece[^1] == (byte)' ';
            }

            position += consumed;
            if (!_removeExtraWhitespaces)
            {
                isPreviousSpace = false;
            }
        }

        var written = output.WrittenSpan;
        if (_removeExtraWhitespaces)
        {
            var space = _escapeWhitespaces ? SpaceSymbol : " "u8;
            while (written.EndsWith(space))
            {
                written = written[..^space.Length];
            }
        }

        var result = Encoding.UTF8.GetString(written);
        if (_treatWhitespaceAsSuffix && _addDummyPrefix)
        {
            result += _escapeWhitespaces ? "▁" : " ";
        }

        return result;
    }

    private void AppendSpace(ArrayBufferWriter<byte> output)
    {
        if (_escapeWhitespaces)
        {
            output.Write(SpaceSymbol);
        }
        else
        {
            output.Write(" "u8);
        }
    }

    /// <summary>Returns the number of input bytes consumed and the bytes that replace them.</summary>
    private int NormalizePrefix(byte[] input, int position, out ReadOnlySpan<byte> replacement)
    {
        var rest = input.AsSpan(position);
        foreach (var symbol in _userDefinedSymbols)
        {
            if (rest.StartsWith(symbol))
            {
                replacement = rest[..symbol.Length];
                return symbol.Length;
            }
        }

        if (_trie is not null)
        {
            var length = LongestRuleMatch(rest, out var offset);
            if (length > 0)
            {
                var value = _replacements.AsSpan(offset);
                var terminator = value.IndexOf((byte)0);
                replacement = terminator < 0 ? value : value[..terminator];
                return length;
            }
        }

        if (Rune.DecodeFromUtf8(rest, out _, out var consumed) != OperationStatus.Done)
        {
            // Mirrors the C++ code: one malformed byte becomes U+FFFD.
            replacement = ReplacementCharacter;
            return 1;
        }

        replacement = rest[..consumed];
        return consumed;
    }

    /// <summary>darts-clone <c>commonPrefixSearch</c>, keeping only the longest match.</summary>
    private int LongestRuleMatch(ReadOnlySpan<byte> key, out int valueOffset)
    {
        var trie = _trie!;
        valueOffset = 0;
        var longest = 0;
        uint node = Offset(trie[0]);
        for (var i = 0; i < key.Length; i++)
        {
            node ^= key[i];
            if (node >= trie.Length)
            {
                break;
            }

            var unit = trie[node];
            if (Label(unit) != key[i])
            {
                break;
            }

            node ^= Offset(unit);
            if (HasLeaf(unit))
            {
                if (node >= trie.Length)
                {
                    break;
                }

                longest = i + 1;
                valueOffset = (int)(trie[node] & 0x7FFFFFFFu);
            }
        }

        return longest;
    }

    private static uint Offset(uint unit) => (unit >> 10) << (int)((unit & (1u << 9)) >> 6);

    private static uint Label(uint unit) => unit & ((1u << 31) | 0xFFu);

    private static bool HasLeaf(uint unit) => ((unit >> 8) & 1) == 1;
}

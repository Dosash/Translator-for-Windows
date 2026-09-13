using System.IO;
using System.Text;
using Translator.Offline;

namespace Translator.Tests.Offline;

/// <summary>Tokenizer behavior on a tiny synthetic .spm model; real-model parity lives in LocalModelTests.</summary>
public class SentencePieceTokenizerTests
{
    private static readonly SentencePieceModel Model = SentencePieceModel.Parse(BuildModel());

    private static readonly Dictionary<string, int> Vocabulary = new()
    {
        ["</s>"] = 0,
        ["<unk>"] = 1,
        ["▁hello"] = 5,
        ["▁world"] = 6,
        ["▁"] = 7,
        ["▁he"] = 8,
        [">>tur<<"] = 9,
        ["<pad>"] = 10,
    };

    [Fact]
    public void ParsesPiecesAndNormalizerSpec()
    {
        Assert.Equal("<unk>", Model.Pieces[0]);
        Assert.Equal(SentencePieceType.Unknown, Model.Types[0]);
        Assert.Equal(-1f, Model.Scores[4]);
        Assert.True(Model.AddDummyPrefix);
        Assert.Null(Model.PrecompiledCharsmap);
    }

    [Theory]
    [InlineData("hello world", "▁hello▁world")]
    [InlineData("   hello    world  ", "▁hello▁world")]
    [InlineData("\t", "▁\t")]
    [InlineData("", "")]
    public void NormalizesWhitespace(string input, string expected) =>
        Assert.Equal(expected, new SentencePieceNormalizer(Model).Normalize(input));

    [Fact]
    public void ViterbiPrefersTheHighestScoringSegmentation()
    {
        var pieces = new List<string>();
        new UnigramModel(Model).Encode("▁hello▁world", pieces);
        Assert.Equal(["▁hello", "▁world"], pieces);
    }

    [Fact]
    public void RunsOfUnknownCharactersBecomeOnePiece()
    {
        var pieces = new List<string>();
        new UnigramModel(Model).Encode("▁hello▁ÿÿ😀", pieces);
        Assert.Equal(["▁hello", "▁", "ÿÿ😀"], pieces);
    }

    [Fact]
    public void MarianEncodingMapsPiecesThroughTheVocabulary()
    {
        var tokenizer = new MarianTokenizer(Model, Vocabulary);
        Assert.Equal([5, 6, 0], tokenizer.Encode("hello world"));
        Assert.Equal([5, 7, 1, 0], tokenizer.Encode("hello ÿÿ"));
        Assert.Equal([9, 5, 0], tokenizer.Encode("hello", ">>tur<<"));
        Assert.Equal([0], tokenizer.Encode(""));
        Assert.Equal(3, tokenizer.CountTokens("hello world"));
    }

    [Fact]
    public void SpecialTokensInTextAreKeptWhole()
    {
        var tokenizer = new MarianTokenizer(Model, Vocabulary);
        Assert.Equal(["▁hello", "</s>", "▁world", "<pad>"], tokenizer.Tokenize("hello</s>world<pad>"));
        Assert.Equal([9, 6, 0], tokenizer.Encode(">>tur<< world"));
    }

    [Fact]
    public void SeparateVocabularyModelsUseSentencePieceIdsOnTheSourceSide()
    {
        var tokenizer = new MarianTokenizer(Model, Vocabulary, sourceIdsFromSentencePiece: true);
        Assert.Equal([4, 7, 2], tokenizer.Encode("hello world"));
        Assert.Equal([4, 3, 0, 2], tokenizer.Encode("hello ÿÿ"));
        Assert.Equal("hello world", tokenizer.Decode([5, 6, 0]));
    }

    [Fact]
    public void DecodingSkipsSpecialTokensAndRestoresSpaces()
    {
        var tokenizer = new MarianTokenizer(Model, Vocabulary);
        Assert.Equal("hello world", tokenizer.Decode([5, 6, 1, 0, 10, 9]));
        Assert.Throws<InvalidOperationException>(() => tokenizer.Encode("hello", ">>xyz<<"));
    }

    private static byte[] BuildModel()
    {
        (string Piece, float Score, SentencePieceType Type)[] pieces =
        [
            ("<unk>", 0, SentencePieceType.Unknown),
            ("<s>", 0, SentencePieceType.Control),
            ("</s>", 0, SentencePieceType.Control),
            ("▁", -2, SentencePieceType.Normal),
            ("▁hello", -1, SentencePieceType.Normal),
            ("▁he", -3, SentencePieceType.Normal),
            ("llo", -3, SentencePieceType.Normal),
            ("▁world", -1.5f, SentencePieceType.Normal),
            .. "helowrd".Select(c => (c.ToString(), -5f, SentencePieceType.Normal)),
        ];

        using var model = new MemoryStream();
        foreach (var (piece, score, type) in pieces)
        {
            using var message = new MemoryStream();
            WriteBytesField(message, 1, Encoding.UTF8.GetBytes(piece));
            WriteVarint(message, (2 << 3) | 5);
            message.Write(BitConverter.GetBytes(score));
            WriteVarint(message, 3 << 3);
            WriteVarint(message, (ulong)type);
            WriteBytesField(model, 1, message.ToArray());
        }

        using (var trainer = new MemoryStream())
        {
            WriteVarint(trainer, 3 << 3);
            WriteVarint(trainer, 1);
            WriteBytesField(model, 2, trainer.ToArray());
        }

        using (var normalizer = new MemoryStream())
        {
            WriteBytesField(normalizer, 1, "identity"u8.ToArray());
            WriteVarint(normalizer, 3 << 3);
            WriteVarint(normalizer, 1);
            WriteBytesField(model, 3, normalizer.ToArray());
        }

        return model.ToArray();
    }

    private static void WriteBytesField(Stream stream, int field, byte[] value)
    {
        WriteVarint(stream, (ulong)((field << 3) | 2));
        WriteVarint(stream, (ulong)value.Length);
        stream.Write(value);
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }
}

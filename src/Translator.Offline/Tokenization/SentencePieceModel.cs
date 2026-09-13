using System.Buffers.Binary;
using System.Text;

namespace Translator.Offline;

internal enum SentencePieceType
{
    Normal = 1,
    Unknown = 2,
    Control = 3,
    UserDefined = 4,
    Unused = 5,
    Byte = 6,
}

/// <summary>
/// The parts of a SentencePiece <c>ModelProto</c> (.spm) needed for encoding, read with a minimal
/// protobuf reader to avoid a protobuf dependency.
/// </summary>
internal sealed class SentencePieceModel
{
    public const int UnigramModelType = 1;

    public required string[] Pieces { get; init; }
    public required float[] Scores { get; init; }
    public required SentencePieceType[] Types { get; init; }
    public int ModelType { get; init; } = UnigramModelType;
    public bool TreatWhitespaceAsSuffix { get; init; }
    public bool ByteFallback { get; init; }
    public byte[]? PrecompiledCharsmap { get; init; }
    public bool AddDummyPrefix { get; init; } = true;
    public bool RemoveExtraWhitespaces { get; init; } = true;
    public bool EscapeWhitespaces { get; init; } = true;

    public static SentencePieceModel Load(string path) => Parse(File.ReadAllBytes(path));

    public static SentencePieceModel Parse(ReadOnlySpan<byte> data)
    {
        var pieces = new List<string>();
        var scores = new List<float>();
        var types = new List<SentencePieceType>();
        var trainer = new TrainerFields();
        var normalizer = new NormalizerFields();

        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (1, ProtoReader.LengthDelimited):
                    var (piece, score, type) = ParsePiece(reader.ReadBytes());
                    pieces.Add(piece);
                    scores.Add(score);
                    types.Add(type);
                    break;
                case (2, ProtoReader.LengthDelimited):
                    trainer = ParseTrainer(reader.ReadBytes());
                    break;
                case (3, ProtoReader.LengthDelimited):
                    normalizer = ParseNormalizer(reader.ReadBytes());
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (pieces.Count == 0)
        {
            throw new InvalidDataException("SentencePiece model has no pieces.");
        }

        return new SentencePieceModel
        {
            Pieces = [.. pieces],
            Scores = [.. scores],
            Types = [.. types],
            ModelType = trainer.ModelType,
            TreatWhitespaceAsSuffix = trainer.TreatWhitespaceAsSuffix,
            ByteFallback = trainer.ByteFallback,
            PrecompiledCharsmap = normalizer.PrecompiledCharsmap,
            AddDummyPrefix = normalizer.AddDummyPrefix,
            RemoveExtraWhitespaces = normalizer.RemoveExtraWhitespaces,
            EscapeWhitespaces = normalizer.EscapeWhitespaces,
        };
    }

    private static (string Piece, float Score, SentencePieceType Type) ParsePiece(ReadOnlySpan<byte> data)
    {
        var piece = "";
        var score = 0f;
        var type = SentencePieceType.Normal;
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (1, ProtoReader.LengthDelimited):
                    piece = Encoding.UTF8.GetString(reader.ReadBytes());
                    break;
                case (2, ProtoReader.Fixed32):
                    score = reader.ReadFloat();
                    break;
                case (3, ProtoReader.Varint):
                    type = (SentencePieceType)(int)reader.ReadVarint();
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        return (piece, score, type);
    }

    private static TrainerFields ParseTrainer(ReadOnlySpan<byte> data)
    {
        var fields = new TrainerFields();
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (3, ProtoReader.Varint):
                    fields.ModelType = (int)reader.ReadVarint();
                    break;
                case (24, ProtoReader.Varint):
                    fields.TreatWhitespaceAsSuffix = reader.ReadVarint() != 0;
                    break;
                case (35, ProtoReader.Varint):
                    fields.ByteFallback = reader.ReadVarint() != 0;
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        return fields;
    }

    private static NormalizerFields ParseNormalizer(ReadOnlySpan<byte> data)
    {
        var fields = new NormalizerFields();
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (2, ProtoReader.LengthDelimited):
                    fields.PrecompiledCharsmap = reader.ReadBytes().ToArray();
                    break;
                case (3, ProtoReader.Varint):
                    fields.AddDummyPrefix = reader.ReadVarint() != 0;
                    break;
                case (4, ProtoReader.Varint):
                    fields.RemoveExtraWhitespaces = reader.ReadVarint() != 0;
                    break;
                case (5, ProtoReader.Varint):
                    fields.EscapeWhitespaces = reader.ReadVarint() != 0;
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        return fields;
    }

    private sealed class TrainerFields
    {
        public int ModelType = UnigramModelType;
        public bool TreatWhitespaceAsSuffix;
        public bool ByteFallback;
    }

    private sealed class NormalizerFields
    {
        public byte[]? PrecompiledCharsmap;
        public bool AddDummyPrefix = true;
        public bool RemoveExtraWhitespaces = true;
        public bool EscapeWhitespaces = true;
    }
}

internal ref struct ProtoReader(ReadOnlySpan<byte> data)
{
    public const int Varint = 0;
    public const int Fixed64 = 1;
    public const int LengthDelimited = 2;
    public const int Fixed32 = 5;

    private readonly ReadOnlySpan<byte> _data = data;
    private int _position;

    public bool TryReadTag(out int field, out int wireType)
    {
        if (_position >= _data.Length)
        {
            field = 0;
            wireType = 0;
            return false;
        }

        var tag = ReadVarint();
        field = (int)(tag >> 3);
        wireType = (int)(tag & 7);
        return true;
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (_position >= _data.Length)
            {
                throw new InvalidDataException("Truncated protobuf varint.");
            }

            var b = _data[_position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException("Malformed protobuf varint.");
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        var length = ReadVarint();
        if (length > (ulong)(_data.Length - _position))
        {
            throw new InvalidDataException("Truncated protobuf field.");
        }

        var slice = _data.Slice(_position, (int)length);
        _position += (int)length;
        return slice;
    }

    public float ReadFloat()
    {
        Require(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data[_position..]);
        _position += 4;
        return value;
    }

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case Varint:
                ReadVarint();
                break;
            case Fixed64:
                Require(8);
                _position += 8;
                break;
            case LengthDelimited:
                ReadBytes();
                break;
            case Fixed32:
                Require(4);
                _position += 4;
                break;
            default:
                throw new InvalidDataException($"Unsupported protobuf wire type {wireType}.");
        }
    }

    private readonly void Require(int bytes)
    {
        if (_data.Length - _position < bytes)
        {
            throw new InvalidDataException("Truncated protobuf field.");
        }
    }
}

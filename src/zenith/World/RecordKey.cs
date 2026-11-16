using System.Buffers.Binary;

namespace Zenith.World;

/// <summary>What a stored record is. The value is the first byte of the key, so records of one kind sit together.</summary>
enum RecordKind : byte
{
    /// <summary>One <see cref="ColumnCodec"/> blob per chunk column (ADR §167).</summary>
    Column = 1,
    Inventory = 2,
    Armor = 3,
    Offhand = 4,
    PlayerData = 5,

    /// <summary>The world's generator identity (<see cref="WorldMetadata"/>). One per world.</summary>
    WorldMetadata = 0x10,

    /// <summary>The world's game rules in <see cref="GameRuleCodec"/> bytes. One per world.</summary>
    GameRules = 0x11,
}

/// <summary>
/// Typed address of a stored record (ADR §168). The on-disk key is binary: the kind byte, then the payload. Column
/// coordinates are big-endian with the sign bit flipped, so byte order equals numeric order and columns that are
/// neighbours in the world are neighbours in the key space.
/// </summary>
readonly record struct RecordKey(RecordKind Kind, long A = 0, long B = 0)
{
    private const int ColumnLength = 1 + 2 * sizeof(int);
    private const int PlayerLength = 1 + 16;

    public static RecordKey Column(ChunkCoord coord) => new(RecordKind.Column, coord.X, coord.Z);

    public static RecordKey Player(RecordKind kind, Guid uuid)
    {
        Span<byte> bytes = stackalloc byte[16];
        uuid.TryWriteBytes(bytes, bigEndian: true, out _);
        return new RecordKey(kind, BinaryPrimitives.ReadInt64BigEndian(bytes), BinaryPrimitives.ReadInt64BigEndian(bytes[8..]));
    }

    public static RecordKey Singleton(RecordKind kind) => new(kind);

    public int EncodedLength => Kind switch
    {
        RecordKind.Column => ColumnLength,
        RecordKind.Inventory or RecordKind.Armor or RecordKind.Offhand or RecordKind.PlayerData => PlayerLength,
        _ => 1,
    };

    /// <summary>Writes the key into <paramref name="destination"/> (at least <see cref="EncodedLength"/> bytes).</summary>
    public void WriteTo(Span<byte> destination)
    {
        destination[0] = (byte)Kind;
        switch (EncodedLength)
        {
            case ColumnLength:
                BinaryPrimitives.WriteUInt32BigEndian(destination[1..], (uint)(int)A ^ 0x8000_0000u);
                BinaryPrimitives.WriteUInt32BigEndian(destination[5..], (uint)(int)B ^ 0x8000_0000u);
                break;
            case PlayerLength:
                BinaryPrimitives.WriteInt64BigEndian(destination[1..], A);
                BinaryPrimitives.WriteInt64BigEndian(destination[9..], B);
                break;
        }
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[EncodedLength];
        WriteTo(bytes);
        return bytes;
    }
}

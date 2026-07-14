namespace Zenith.Nbt;

/// <summary>Bedrock NBT tag type IDs (wire discriminant).</summary>
public enum NbtType : byte
{
    End = 0,
    Byte = 1,
    Short = 2,
    Int = 3,
    Long = 4,
    Float = 5,
    Double = 6,
    ByteArray = 7,
    String = 8,
    List = 9,
    Compound = 10,
    IntArray = 11,
    LongArray = 12
}

/// <summary>
/// Wire integer/string length encoding.
/// LittleEndian = Bedrock disk LE; Network = zigzag varint; BigEndian = Java/BDS palette dumps.
/// </summary>
public enum NbtEncoding : byte
{
    LittleEndian = 0,
    Network = 1,
    BigEndian = 2
}

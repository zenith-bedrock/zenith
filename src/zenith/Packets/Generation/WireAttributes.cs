using Zenith.Raknet.Stream;

namespace Zenith.Packets.Generation;

/// <summary>
/// Opts a <c>partial class : DataPacket</c> into generator-driven Encode/Decode/Id.
/// Classes without this attribute are never touched by zenith.PacketGenerator.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GamePacketAttribute(int protocolId) : Attribute
{
    public int ProtocolId { get; } = protocolId;
}

/// <summary>
/// Fixed-width numeric/bool field (byte/short/int/long/float/double/bool, or an enum whose
/// underlying type is one of those). Default endianness mirrors BinaryStream's own default
/// (Big) - Bedrock's wire format is almost always Little for these fields, so callers state
/// it explicitly rather than the generator guessing.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireAttribute(BinaryStream.Endianess endianess = BinaryStream.Endianess.Big) : Attribute
{
    public BinaryStream.Endianess Endianess { get; } = endianess;
}

/// <summary>LEB128 family (UnsignedVarInt/VarInt zigzag/UnsignedVarLong/VarLong) - no endianness, resolved by CLR type.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireVarAttribute : Attribute;

/// <summary>string property. Default is varint-length-prefixed (Bedrock-standard VarString); set Utf16LengthPrefixed for the rare u16-len-prefixed String() variant.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireStringAttribute(bool utf16LengthPrefixed = false) : Attribute
{
    public bool Utf16LengthPrefixed { get; } = utf16LengthPrefixed;
}

/// <summary>byte[] via ReadByteArray/WriteByteArray (varint-length blob, not UTF-8).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireByteArrayAttribute : Attribute;

/// <summary>Guid via ReadUuid/WriteUuid (Bedrock byte-swapped layout).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireUuidAttribute : Attribute;

/// <summary>
/// Nested sub-object. Resolved by the generator via symbol lookup: prefers
/// <c>static T Read(ref BinaryStream)</c> + instance <c>void Write(ref BinaryStream)</c>
/// (SerializedSkin/CommandOriginData convention); falls back to a parameterless constructor
/// + instance <c>void Decode(ref BinaryStream)</c> (HeaderInfo convention) for the read side.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireNestedAttribute : Attribute;

public enum CountEncoding
{
    UnsignedVarInt,
    FixedByte,
    FixedUShort,
    FixedUInt
}

/// <summary>Array/collection of [WireNested]-shaped elements. CountEncoding defaults to the Bedrock-standard UnsignedVarInt; override for outliers with a fixed-width count.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireNestedArrayAttribute(
    CountEncoding countEncoding = CountEncoding.UnsignedVarInt,
    BinaryStream.Endianess countEndianess = BinaryStream.Endianess.Little) : Attribute
{
    public CountEncoding CountEncoding { get; } = countEncoding;
    public BinaryStream.Endianess CountEndianess { get; } = countEndianess;
}

/// <summary>
/// Combine with a base kind attribute (e.g. [WireString][WireOptional] string? Foo) to get the
/// has-value-flag-then-value pattern already used by hand-written packets like
/// ModalFormResponsePacket. Property type must be CLR-nullable.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireOptionalAttribute : Attribute;

/// <summary>
/// Field only present on the wire when an earlier-declared property equals <paramref name="value"/>
/// (e.g. MovePlayerPacket-style teleport-only fields). <paramref name="otherProperty"/> must be a
/// [Wire*]-attributed property declared earlier in the same class - enforced by diagnostic.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireWhenAttribute(string otherProperty, object value) : Attribute
{
    public string OtherProperty { get; } = otherProperty;
    public object Value { get; } = value;
}

/// <summary>Escape hatch: property is skipped entirely by the generator (no read, no write).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireIgnoreAttribute : Attribute;

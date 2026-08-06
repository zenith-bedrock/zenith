namespace Zenith.PacketGenerator;

/// <summary>
/// CLR-type-name -&gt; BinaryStream wire-method vocabulary, shared (via a linked file, not a
/// ProjectReference - see below) between zenith.PacketGenerator, which reads [Wire*] attributes
/// and emits Encode/Decode, and tools/protocol-import, which scaffolds those same attributes
/// from public protocol docs. Single source of truth for "int"-&gt;"Int"/"uint"-&gt;"UInt" etc. -
/// previously duplicated independently in both (GamePacketGenerator.WireMethod +
/// AttributeScaffolder.FixedSuffix, and their WireVar equivalents), with no compiler safety net
/// on the scaffolder's copy if this ever needed a new entry (see ADR §76 DRY audit).
///
/// Linked rather than referenced: zenith.PacketGenerator is a netstandard2.0 Roslyn analyzer
/// (can't carry a normal ProjectReference with runtime dependencies), tools/protocol-import is a
/// net10.0 exe - incompatible TFMs for a shared library. This file has zero external
/// dependencies, so `&lt;Compile Include&gt;` links it into both without either constraint.
/// </summary>
internal static class WireTypeVocabulary
{
    /// <summary>Fixed-width [Wire] suffix ("int" -&gt; WriteInt/ReadInt) and whether that
    /// BinaryStream method takes an Endianess argument (Bool/Byte don't).</summary>
    public static (string Suffix, bool HasEndian) FixedSuffix(string clrType) => clrType switch
    {
        "bool" => ("Bool", false),
        "byte" => ("Byte", false),
        "short" => ("Short", true),
        "ushort" => ("UShort", true),
        "int" => ("Int", true),
        "uint" => ("UInt", true),
        "long" => ("Long", true),
        "ulong" => ("ULong", true),
        "float" => ("Float", true),
        "double" => ("Double", true),
        _ => ("Int", true)
    };

    /// <summary>[WireVar] method suffix ("uint" -&gt; WriteUnsignedVarInt/ReadUnsignedVarInt) and
    /// the storage type BinaryStream's Unsigned* methods actually take/return (null when the
    /// CLR type matches the storage type directly, e.g. VarInt already takes/returns int) - the
    /// caller casts the value to/from StorageCastType around the Write/Read call.</summary>
    public static (string MethodSuffix, string? StorageCastType) VarInfo(string clrType) => clrType switch
    {
        "int" => ("VarInt", null),
        "uint" => ("UnsignedVarInt", "int"),
        "long" => ("VarLong", null),
        "ulong" => ("UnsignedVarLong", "long"),
        _ => ("VarInt", null)
    };
}

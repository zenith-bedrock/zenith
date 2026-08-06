namespace Zenith.PacketGenerator;

internal enum WireKind
{
    Wire,
    WireVar,
    WireString,
    WireByteArray,
    WireUuid,
    WireNested,
    WireNestedArray
}

internal sealed class FieldModel
{
    public string PropertyName { get; set; } = "";
    public WireKind Kind { get; set; }
    public string ClrTypeName { get; set; } = "";
    public bool IsEnum { get; set; }
    public string? EnumUnderlyingTypeName { get; set; }
    public string Endianess { get; set; } = "Big";
    public bool Utf16LengthPrefixed { get; set; }
    public bool IsUnsignedVar { get; set; }
    public string? NestedTypeName { get; set; }
    public bool NestedHasStaticRead { get; set; }
    public string CountEncoding { get; set; } = "UnsignedVarInt";
    public string CountEndianess { get; set; } = "Little";
    public bool IsOptional { get; set; }
    public bool IsValueTypeNullable { get; set; }
    public string? WhenOtherProperty { get; set; }
    public string? WhenValueLiteral { get; set; }
}

internal sealed class PacketModel
{
    public string Namespace { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int ProtocolId { get; set; }
    public string FullyQualifiedName { get; set; } = "";
    public string HintName { get; set; } = "";
    public List<FieldModel> Fields { get; } = [];
}

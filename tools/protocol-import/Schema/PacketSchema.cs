namespace Zenith.ProtocolImport.Schema;

internal enum SchemaConstruct
{
    Scalar,
    Array,
    Union,
    Constant,
    Unknown
}

/// <summary>
/// Lossless-at-the-boundary field model. Type remains the codegen-facing resolved type, while
/// Construct/Reference/UnsupportedReason retain why a source field cannot yet be generated.
/// </summary>
internal sealed record FieldSchema(
    string Name,
    string Type,
    string? EnumName,
    bool Optional,
    string? RepeatPrefix,
    bool IsConstantLiteral = false,
    bool IsComplexType = false,
    SchemaConstruct Construct = SchemaConstruct.Scalar,
    string? Reference = null,
    string? UnsupportedReason = null);

internal sealed record PacketSchema(
    int Id,
    string Name,
    IReadOnlyList<FieldSchema> Fields);

internal sealed record TypeSchema(
    string Name,
    IReadOnlyList<FieldSchema> Fields);

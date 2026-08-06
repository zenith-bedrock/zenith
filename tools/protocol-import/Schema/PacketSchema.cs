namespace Zenith.ProtocolImport.Schema;

internal sealed record FieldSchema(
    string Name,
    string Type,
    string? EnumName,
    bool Optional,
    string? RepeatPrefix,
    bool IsConstantLiteral = false,
    bool IsComplexType = false);

internal sealed record PacketSchema(
    int Id,
    string Name,
    IReadOnlyList<FieldSchema> Fields);

internal sealed record TypeSchema(
    string Name,
    IReadOnlyList<FieldSchema> Fields);

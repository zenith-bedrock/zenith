using Microsoft.CodeAnalysis;

namespace Zenith.PacketGenerator;

internal static class Diagnostics
{
    private const string Category = "ZenithPacketGenerator";

    public static readonly DiagnosticDescriptor NotPartial = new(
        "ZPG001", "GamePacket class must be partial",
        "Class '{0}' must be declared partial to use [GamePacket]",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NotDataPacket = new(
        "ZPG002", "GamePacket class must derive from DataPacket",
        "Class '{0}' must derive from Zenith.Packets.DataPacket to use [GamePacket]",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ConflictingHandWritten = new(
        "ZPG003", "GamePacket class must not hand-write Encode/Decode",
        "Remove the hand-written Encode/Decode from '{0}' or remove [GamePacket] and its Wire* attributes",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "ZPG004", "Wire attribute applied to an unsupported CLR type",
        "Property '{0}.{1}' has a wire attribute that does not support its type '{2}'",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MultipleBaseAttributes = new(
        "ZPG005", "Property has more than one base wire-kind attribute",
        "Property '{0}.{1}' must carry exactly one of [Wire]/[WireVar]/[WireString]/[WireByteArray]/[WireUuid]/[WireNested]/[WireNestedArray]",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NestedMissingRead = new(
        "ZPG006", "[WireNested] element type missing a usable read shape",
        "Type '{0}' used with [WireNested]/[WireNestedArray] on '{1}.{2}' must expose 'static {0} Read(ref BinaryStream)' or a parameterless constructor with instance 'void Decode(ref BinaryStream)'",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NestedMissingWrite = new(
        "ZPG007", "[WireNested] element type missing a usable write shape",
        "Type '{0}' used with [WireNested]/[WireNestedArray] on '{1}.{2}' must expose instance 'void Write(ref BinaryStream)'",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor OptionalNotNullable = new(
        "ZPG008", "[WireOptional] applied to a non-nullable property",
        "Property '{0}.{1}' has [WireOptional] but its type '{2}' is not nullable",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor WhenUnresolvedProperty = new(
        "ZPG009", "[WireWhen] references an unknown wire property",
        "Property '{0}' referenced by [WireWhen] on '{1}.{2}' is not a wire-attributed property on the same class",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor WhenOutOfOrder = new(
        "ZPG010", "[WireWhen] references a property declared later",
        "Property '{0}' referenced by [WireWhen] on '{1}.{2}' must be declared earlier in '{1}'",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor WhenValueNotConvertible = new(
        "ZPG011", "[WireWhen] value is not convertible to the referenced property's type",
        "The value passed to [WireWhen] on '{0}.{1}' is not convertible to the type of '{2}'",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NoWireFields = new(
        "ZPG012", "GamePacket class has nothing to generate",
        "Class '{0}' has [GamePacket] but no Wire*-attributed properties - remove the attribute or add fields",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor DuplicateProtocolId = new(
        "ZPG013", "Duplicate [GamePacket] protocol id",
        "Protocol id {0} is used by both '{1}' and '{2}'",
        Category, DiagnosticSeverity.Error, true);
}

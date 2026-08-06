using System.Text;
using Zenith.PacketGenerator;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Scaffolding;

internal sealed record ScaffoldResult(string SourceText, IReadOnlyList<string> Notes);

/// <summary>
/// Maps a PacketSchema (+ one level of nested-type resolution) onto the [GamePacket]/[Wire*]
/// vocabulary from src/zenith/Packets/Generation/WireAttributes.cs. Scaffolding aid only - see
/// ADR §76 v1 scope. Output is meant for human review, never auto-applied.
/// </summary>
internal sealed class AttributeScaffolder(ISchemaSource source, string cacheDir)
{
    public ScaffoldResult Scaffold(PacketSchema packet)
    {
        var notes = new List<string>();
        var nestedTypes = new StringBuilder();
        var properties = new StringBuilder();

        foreach (var field in packet.Fields)
        {
            var propertyName = PropertyNamer.ToPascalCase(field.Name);
            EmitField(field, propertyName, properties, nestedTypes, notes);
        }

        var body = new StringBuilder();
        body.AppendLine("using Zenith.Packets.Generation;");
        body.AppendLine("using Zenith.Raknet.Stream;");
        body.AppendLine();
        body.AppendLine("namespace Zenith.Packets;");
        body.AppendLine();
        if (nestedTypes.Length > 0)
        {
            body.Append(nestedTypes);
            body.AppendLine();
        }
        body.AppendLine($"// Scaffolded by protocol-import from {source.Name} \"{packet.Name}\" (id {packet.Id}).");
        body.AppendLine("// Review before committing - see ADR §76 in docs/decisions.md for what this tool does and does not handle.");
        body.AppendLine($"[GamePacket({packet.Id})]");
        body.AppendLine($"sealed partial class {packet.Name} : DataPacket");
        body.AppendLine("{");
        body.Append(properties);
        body.AppendLine("}");

        return new ScaffoldResult(body.ToString(), notes);
    }

    private void EmitField(
        FieldSchema field, string propertyName,
        StringBuilder properties, StringBuilder nestedTypes, List<string> notes)
    {
        if (field.IsConstantLiteral)
        {
            notes.Add($"Field '{field.Name}' (type '{field.Type}'): wire-present constant/tag, not a settable property - matches the StartGamePacket-style literal-placeholder pattern (ADR §76). Skipped, not scaffolded.");
            properties.AppendLine($"    // SKIPPED: constant/tag field (type '{field.Type}') - wire-present literal, not a real property (see ADR §76)");
            properties.AppendLine();
            return;
        }

        if (field.IsComplexType)
        {
            notes.Add($"Field '{field.Name}': type is a discriminated union/complex object in the schema (not a plain type name) - the importer doesn't model unions in v1. Left as TODO.");
            properties.AppendLine($"    // TODO: discriminated-union field '{field.Name}' - importer doesn't model unions in v1, resolve by hand (see ADR §76)");
            properties.AppendLine($"    // public object? {propertyName} {{ get; set; }}");
            properties.AppendLine();
            return;
        }

        if (field.RepeatPrefix is not null)
        {
            EmitArrayField(field, propertyName, properties, nestedTypes, notes);
            return;
        }

        var emission = KnownTypeMap.Resolve(field.Type);
        if (emission.Kind == WireEmissionKind.Unknown)
        {
            if (TryResolveNestedType(field.Type, nestedTypes, notes))
            {
                EmitAttributeLine(properties, "[WireNested]", field.Optional);
                properties.AppendLine($"    public {NullableSuffix(field.Type, field.Optional)} {propertyName} {{ get; set; }}");
                properties.AppendLine();
                return;
            }

            notes.Add($"Field '{field.Name}': unknown type '{field.Type}' - not in KnownTypeMap and not resolvable as a simple nested type. Left as TODO.");
            properties.AppendLine($"    // TODO: resolve type '{field.Type}' by hand (see KnownTypeMap / ADR §76)");
            properties.AppendLine($"    // public object? {propertyName} {{ get; set; }}");
            properties.AppendLine();
            return;
        }

        var attr = emission.Kind switch
        {
            WireEmissionKind.Fixed => emission.Endianess is not null
                ? $"[Wire(BinaryStream.Endianess.{emission.Endianess})]"
                : "[Wire]",
            WireEmissionKind.Var => "[WireVar]",
            WireEmissionKind.String => "[WireString]",
            WireEmissionKind.Uuid => "[WireUuid]",
            WireEmissionKind.ByteArray => "[WireByteArray]",
            _ => throw new InvalidOperationException()
        };

        EmitAttributeLine(properties, attr, field.Optional);
        var clrType = field.Optional ? $"{emission.ClrType}?" : emission.ClrType;
        var defaultSuffix = field.Optional ? "" : emission.ClrType switch
        {
            "string" => " = \"\";",
            "byte[]" => " = [];",
            _ => ""
        };
        properties.AppendLine($"    public {clrType} {propertyName} {{ get; set; }}{defaultSuffix}");
        properties.AppendLine();
    }

    private void EmitArrayField(
        FieldSchema field, string propertyName,
        StringBuilder properties, StringBuilder nestedTypes, List<string> notes)
    {
        var elementEmission = KnownTypeMap.Resolve(field.Type);
        var countEncoding = field.RepeatPrefix switch
        {
            "uvarint32" or "varint32" => "CountEncoding.UnsignedVarInt",
            "uint8" => "CountEncoding.FixedByte",
            "uint16" => "CountEncoding.FixedUShort",
            "uint32" => "CountEncoding.FixedUInt",
            _ => "CountEncoding.UnsignedVarInt"
        };

        if (field.RepeatPrefix is not (null or "uvarint32" or "varint32" or "uint8" or "uint16" or "uint32"))
        {
            notes.Add($"Field '{field.Name}': source schema doesn't specify an array count-encoding - assumed UnsignedVarInt (Bedrock default). Verify against the real packet before committing.");
        }

        if (elementEmission.Kind != WireEmissionKind.Unknown)
        {
            notes.Add($"Field '{field.Name}': array of primitive '{field.Type}' - [WireNestedArray] requires named-type elements with Read/Write (see ADR §76 EmoteListPacket note). Left as TODO.");
            properties.AppendLine($"    // TODO: primitive-element array (type '{field.Type}'), not covered by [WireNestedArray] v1 - see ADR §76 EmoteListPacket note");
            properties.AppendLine($"    // public {elementEmission.ClrType}[] {propertyName} {{ get; set; }} = [];");
            properties.AppendLine();
            return;
        }

        if (!TryResolveNestedType(field.Type, nestedTypes, notes))
        {
            notes.Add($"Field '{field.Name}': array of unresolvable type '{field.Type}'. Left as TODO.");
            properties.AppendLine($"    // TODO: resolve array element type '{field.Type}' by hand");
            properties.AppendLine($"    // public object[] {propertyName} {{ get; set; }} = [];");
            properties.AppendLine();
            return;
        }

        properties.AppendLine($"    [WireNestedArray({countEncoding})]");
        properties.AppendLine($"    public {field.Type}[] {propertyName} {{ get; set; }} = [];");
        properties.AppendLine();
    }

    /// <summary>
    /// One level only: resolves types/{typeName}.json and scaffolds a readonly record struct
    /// with static Read/instance Write iff every one of its own fields is a known-simple type
    /// (no recursion into further unknown nested types, no arrays inside nested types in v1).
    /// </summary>
    private bool TryResolveNestedType(string typeName, StringBuilder nestedTypes, List<string> notes)
    {
        var type = source.ReadType(cacheDir, typeName);
        if (type is null) return false;

        var fieldPlans = new List<(string PropertyName, FieldSchema Field, WireEmission Emission)>();
        foreach (var f in type.Fields)
        {
            if (f.RepeatPrefix is not null) return false;
            var emission = KnownTypeMap.Resolve(f.Type);
            if (emission.Kind == WireEmissionKind.Unknown) return false;
            fieldPlans.Add((PropertyNamer.ToPascalCase(f.Name), f, emission));
        }

        if (fieldPlans.Count == 0) return false;

        var sb = new StringBuilder();
        sb.AppendLine($"readonly struct {typeName}");
        sb.AppendLine("{");
        foreach (var (propName, f, emission) in fieldPlans)
        {
            var clrType = f.Optional ? $"{emission.ClrType}?" : emission.ClrType;
            sb.AppendLine($"    public {clrType} {propName} {{ get; init; }}");
        }
        sb.AppendLine();
        sb.AppendLine($"    public static {typeName} Read(ref BinaryStream stream) => new()");
        sb.AppendLine("    {");
        foreach (var (propName, f, emission) in fieldPlans)
            sb.AppendLine($"        {propName} = {ReadExpr(emission, f.Optional)},");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    public void Write(ref BinaryStream writer)");
        sb.AppendLine("    {");
        foreach (var (propName, _, emission) in fieldPlans)
            sb.AppendLine($"        {WriteStmt(emission, propName)}");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        nestedTypes.Append(sb);
        notes.Add($"Scaffolded nested type '{typeName}' from {source.Name}'s '{typeName}' type definition - review before committing.");
        return true;
    }

    private static string ReadExpr(WireEmission emission, bool optional)
    {
        if (optional)
            return "default"; // nested-type-field optionality isn't modeled in v1 - flagged for review via notes.

        return emission.Kind switch
        {
            WireEmissionKind.Fixed when emission.Endianess is not null =>
                $"stream.Read{FixedSuffix(emission.ClrType)}(BinaryStream.Endianess.{emission.Endianess})",
            WireEmissionKind.Fixed => $"stream.Read{FixedSuffix(emission.ClrType)}()",
            WireEmissionKind.Var => VarReadExpr(emission.ClrType),
            WireEmissionKind.String => "stream.ReadVarString()",
            WireEmissionKind.Uuid => "stream.ReadUuid()",
            WireEmissionKind.ByteArray => "stream.ReadByteArray()",
            _ => "default"
        };
    }

    private static string WriteStmt(WireEmission emission, string propName)
    {
        return emission.Kind switch
        {
            WireEmissionKind.Fixed when emission.Endianess is not null =>
                $"writer.Write{FixedSuffix(emission.ClrType)}({propName}, BinaryStream.Endianess.{emission.Endianess});",
            WireEmissionKind.Fixed => $"writer.Write{FixedSuffix(emission.ClrType)}({propName});",
            WireEmissionKind.Var => VarWriteStmt(emission.ClrType, propName),
            WireEmissionKind.String => $"writer.WriteVarString({propName});",
            WireEmissionKind.Uuid => $"writer.WriteUuid({propName});",
            WireEmissionKind.ByteArray => $"writer.WriteByteArray({propName});",
            _ => $"// TODO write {propName}"
        };
    }

    private static string FixedSuffix(string clrType) => WireTypeVocabulary.FixedSuffix(clrType).Suffix;

    private static string VarReadExpr(string clrType)
    {
        var (methodSuffix, storageCast) = WireTypeVocabulary.VarInfo(clrType);
        return storageCast is null ? $"stream.Read{methodSuffix}()" : $"({clrType})stream.Read{methodSuffix}()";
    }

    private static string VarWriteStmt(string clrType, string propName)
    {
        var (methodSuffix, storageCast) = WireTypeVocabulary.VarInfo(clrType);
        var argExpr = storageCast is null ? propName : $"({storageCast}){propName}";
        return $"writer.Write{methodSuffix}({argExpr});";
    }

    private static void EmitAttributeLine(StringBuilder properties, string baseAttr, bool optional)
    {
        properties.AppendLine($"    {baseAttr}");
        if (optional) properties.AppendLine("    [WireOptional]");
    }

    private static string NullableSuffix(string typeName, bool optional) => optional ? $"{typeName}?" : typeName;
}

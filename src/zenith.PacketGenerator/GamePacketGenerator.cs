using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zenith.PacketGenerator;

/// <summary>
/// Mechanizes DataPacket.Encode/Decode/Id for classes marked [GamePacket]. Emits exactly the
/// BinaryStream calls a human would write by hand - no reflection, no runtime abstraction.
/// See docs/decisions.md §76.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GamePacketGenerator : IIncrementalGenerator
{
    private const string GamePacketAttributeName = "Zenith.Packets.Generation.GamePacketAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var packets = context.SyntaxProvider.ForAttributeWithMetadataName(
            GamePacketAttributeName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => Analyze(ctx, ct));

        context.RegisterSourceOutput(packets, static (spc, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

            if (result.Model is not null && !result.HasError)
                spc.AddSource(result.Model.HintName, SourceText(result.Model));
        });

        var idInfos = packets
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => (r.Model!.ProtocolId, r.Model.FullyQualifiedName, r.ClassLocation))
            .Collect();

        context.RegisterSourceOutput(idInfos, static (spc, items) =>
        {
            foreach (var group in items.GroupBy(i => i.ProtocolId))
            {
                var list = group.ToList();
                if (list.Count < 2) continue;
                for (var i = 1; i < list.Count; i++)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DuplicateProtocolId, list[i].ClassLocation,
                        group.Key, list[0].FullyQualifiedName, list[i].FullyQualifiedName));
                }
            }
        });
    }

    private sealed class AnalysisResult
    {
        public PacketModel? Model { get; set; }
        public bool HasError { get; set; }
        public List<Diagnostic> Diagnostics { get; } = [];
        public int ProtocolId { get; set; }
        public Location ClassLocation { get; set; } = Location.None;
    }

    private static AnalysisResult Analyze(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var classNode = (ClassDeclarationSyntax)ctx.TargetNode;
        var classLocation = classNode.Identifier.GetLocation();
        var result = new AnalysisResult { ClassLocation = classLocation };
        var displayName = classSymbol.ToDisplayString();
        var hasError = false;

        if (!classNode.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            result.Diagnostics.Add(Diagnostic.Create(Diagnostics.NotPartial, classLocation, displayName));
            hasError = true;
        }

        if (!DerivesFromDataPacket(classSymbol))
        {
            result.Diagnostics.Add(Diagnostic.Create(Diagnostics.NotDataPacket, classLocation, displayName));
            hasError = true;
        }

        if (HasHandWrittenEncodeOrDecode(classSymbol))
        {
            result.Diagnostics.Add(Diagnostic.Create(Diagnostics.ConflictingHandWritten, classLocation, displayName));
            hasError = true;
        }

        var attribute = ctx.Attributes[0];
        var protocolId = attribute.ConstructorArguments.Length > 0
            ? (int)(attribute.ConstructorArguments[0].Value ?? 0)
            : 0;

        var model = new PacketModel
        {
            Namespace = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : classSymbol.ContainingNamespace.ToDisplayString(),
            ClassName = classSymbol.Name,
            ProtocolId = protocolId,
            FullyQualifiedName = displayName,
            HintName = $"{classSymbol.Name}.g.cs"
        };

        var addedFieldNames = new List<string>();

        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var attrs = member.GetAttributes();
            if (attrs.Any(a => a.AttributeClass?.Name == "WireIgnoreAttribute"))
                continue;

            var baseAttrs = attrs.Where(a => a.AttributeClass?.Name is
                "WireAttribute" or "WireVarAttribute" or "WireStringAttribute" or
                "WireByteArrayAttribute" or "WireUuidAttribute" or "WireNestedAttribute" or
                "WireNestedArrayAttribute").ToList();

            if (baseAttrs.Count == 0)
                continue;

            if (baseAttrs.Count > 1)
            {
                result.Diagnostics.Add(Diagnostic.Create(Diagnostics.MultipleBaseAttributes,
                    member.Locations.FirstOrDefault() ?? classLocation, displayName, member.Name));
                hasError = true;
                continue;
            }

            var baseAttr = baseAttrs[0];
            var optionalAttr = attrs.FirstOrDefault(a => a.AttributeClass?.Name == "WireOptionalAttribute");
            var whenAttr = attrs.FirstOrDefault(a => a.AttributeClass?.Name == "WireWhenAttribute");

            var propLocation = member.Locations.FirstOrDefault() ?? classLocation;
            var field = BuildField(member, baseAttr, optionalAttr is not null, propLocation, displayName, result);
            if (field is null)
            {
                hasError = true;
                continue;
            }

            if (whenAttr is not null)
            {
                var otherName = whenAttr.ConstructorArguments.Length > 0
                    ? whenAttr.ConstructorArguments[0].Value as string
                    : null;

                if (otherName is null || !addedFieldNames.Contains(otherName))
                {
                    var declaredLater = classSymbol.GetMembers().OfType<IPropertySymbol>()
                        .Any(p => p.Name == otherName);
                    result.Diagnostics.Add(Diagnostic.Create(
                        declaredLater ? Diagnostics.WhenOutOfOrder : Diagnostics.WhenUnresolvedProperty,
                        propLocation, otherName ?? "?", displayName, member.Name));
                    hasError = true;
                    continue;
                }

                field.WhenOtherProperty = otherName;
                field.WhenValueLiteral = whenAttr.ConstructorArguments.Length > 1
                    ? FormatConstant(whenAttr.ConstructorArguments[1])
                    : "default";
            }

            model.Fields.Add(field);
            addedFieldNames.Add(member.Name);
        }

        if (model.Fields.Count == 0 && !hasError)
        {
            result.Diagnostics.Add(Diagnostic.Create(Diagnostics.NoWireFields, classLocation, displayName));
        }

        result.Model = model;
        result.HasError = hasError;
        result.ProtocolId = protocolId;
        return result;
    }

    private static bool DerivesFromDataPacket(INamedTypeSymbol classSymbol)
    {
        var baseType = classSymbol.BaseType;
        while (baseType is not null)
        {
            if (baseType.Name == "DataPacket") return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static bool HasHandWrittenEncodeOrDecode(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Any(m => m.Name is "Encode" or "Decode" &&
                      SymbolEqualityComparer.Default.Equals(m.ContainingType, classSymbol));
    }

    private static FieldModel? BuildField(
        IPropertySymbol member, AttributeData baseAttr, bool isOptional,
        Location location, string classDisplayName, AnalysisResult result)
    {
        var kind = baseAttr.AttributeClass!.Name switch
        {
            "WireAttribute" => WireKind.Wire,
            "WireVarAttribute" => WireKind.WireVar,
            "WireStringAttribute" => WireKind.WireString,
            "WireByteArrayAttribute" => WireKind.WireByteArray,
            "WireUuidAttribute" => WireKind.WireUuid,
            "WireNestedAttribute" => WireKind.WireNested,
            "WireNestedArrayAttribute" => WireKind.WireNestedArray,
            _ => throw new InvalidOperationException()
        };

        var propertyType = member.Type;
        var isValueTypeNullable = false;

        if (propertyType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt)
        {
            isValueTypeNullable = true;
            propertyType = nt.TypeArguments[0];
        }

        if (isOptional)
        {
            var nullableOk = isValueTypeNullable ||
                              (propertyType.IsReferenceType && member.Type.NullableAnnotation == NullableAnnotation.Annotated);
            if (!nullableOk)
            {
                result.Diagnostics.Add(Diagnostic.Create(Diagnostics.OptionalNotNullable,
                    location, classDisplayName, member.Name, member.Type.ToDisplayString()));
                return null;
            }
        }

        var field = new FieldModel
        {
            PropertyName = member.Name,
            Kind = kind,
            ClrTypeName = member.Type.ToDisplayString(),
            IsOptional = isOptional,
            IsValueTypeNullable = isValueTypeNullable
        };

        switch (kind)
        {
            case WireKind.Wire:
            case WireKind.WireVar:
            {
                var underlying = propertyType;
                var isEnum = false;
                string? enumUnderlyingName = null;
                if (underlying.TypeKind == TypeKind.Enum)
                {
                    isEnum = true;
                    var enumUnderlying = ((INamedTypeSymbol)underlying).EnumUnderlyingType!;
                    enumUnderlyingName = enumUnderlying.ToDisplayString();
                    underlying = enumUnderlying;
                }

                if (!IsSupportedNumericOrBool(underlying.SpecialType, kind))
                {
                    result.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedType,
                        location, classDisplayName, member.Name, member.Type.ToDisplayString()));
                    return null;
                }

                field.IsEnum = isEnum;
                field.EnumUnderlyingTypeName = enumUnderlyingName;
                if (kind == WireKind.Wire)
                {
                    var endianessArg = baseAttr.ConstructorArguments.Length > 0
                        ? baseAttr.ConstructorArguments[0].Value
                        : 0;
                    field.Endianess = Convert.ToInt32(endianessArg) == 1 ? "Little" : "Big";
                }
                else
                {
                    field.IsUnsignedVar = baseAttr.ConstructorArguments.Length > 0 &&
                                           baseAttr.ConstructorArguments[0].Value is true;
                }

                break;
            }
            case WireKind.WireString:
            {
                if (propertyType.SpecialType != SpecialType.System_String)
                {
                    result.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedType,
                        location, classDisplayName, member.Name, member.Type.ToDisplayString()));
                    return null;
                }
                field.Utf16LengthPrefixed = baseAttr.ConstructorArguments.Length > 0 &&
                                             baseAttr.ConstructorArguments[0].Value is true;
                break;
            }
            case WireKind.WireByteArray:
            {
                if (propertyType is not IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
                {
                    result.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedType,
                        location, classDisplayName, member.Name, member.Type.ToDisplayString()));
                    return null;
                }
                break;
            }
            case WireKind.WireUuid:
            {
                if (propertyType.Name != "Guid" || propertyType.ContainingNamespace?.ToDisplayString() != "System")
                {
                    result.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedType,
                        location, classDisplayName, member.Name, member.Type.ToDisplayString()));
                    return null;
                }
                break;
            }
            case WireKind.WireNested:
            {
                if (propertyType is not INamedTypeSymbol nestedType || propertyType.TypeKind == TypeKind.Array)
                {
                    result.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedType,
                        location, classDisplayName, member.Name, member.Type.ToDisplayString()));
                    return null;
                }

                if (!ResolveNestedShape(nestedType, member.Name, classDisplayName, location, result,
                        out var hasStaticRead))
                    return null;

                field.NestedTypeName = nestedType.ToDisplayString();
                field.NestedHasStaticRead = hasStaticRead;

                if (isOptional && !hasStaticRead)
                {
                    result.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedType,
                        location, classDisplayName, member.Name,
                        "[WireOptional] on [WireNested] requires the static Read(ref BinaryStream) factory shape"));
                    return null;
                }
                break;
            }
            case WireKind.WireNestedArray:
            {
                if (propertyType is not IArrayTypeSymbol arrayType || arrayType.ElementType.TypeKind == TypeKind.Array)
                {
                    result.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedType,
                        location, classDisplayName, member.Name, member.Type.ToDisplayString()));
                    return null;
                }

                if (isOptional)
                {
                    result.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedType,
                        location, classDisplayName, member.Name,
                        "[WireOptional] is not supported on [WireNestedArray] properties"));
                    return null;
                }

                if (arrayType.ElementType is not INamedTypeSymbol elementType ||
                    !ResolveNestedShape(elementType, member.Name, classDisplayName, location, result, out var elemHasStaticRead))
                    return null;

                field.NestedTypeName = elementType.ToDisplayString();
                field.NestedHasStaticRead = elemHasStaticRead;

                var countEncoding = baseAttr.ConstructorArguments.Length > 0
                    ? Convert.ToInt32(baseAttr.ConstructorArguments[0].Value)
                    : 0;
                var countEndianess = baseAttr.ConstructorArguments.Length > 1
                    ? Convert.ToInt32(baseAttr.ConstructorArguments[1].Value)
                    : 1;

                field.CountEncoding = countEncoding switch
                {
                    1 => "FixedByte",
                    2 => "FixedUShort",
                    3 => "FixedUInt",
                    _ => "UnsignedVarInt"
                };
                field.CountEndianess = countEndianess == 1 ? "Little" : "Big";
                break;
            }
        }

        return field;
    }

    private static bool ResolveNestedShape(
        INamedTypeSymbol nestedType, string propertyName, string classDisplayName, Location location,
        AnalysisResult result, out bool hasStaticRead)
    {
        hasStaticRead = nestedType.GetMembers("Read").OfType<IMethodSymbol>()
            .Any(m => m.IsStatic && m.Parameters.Length == 1 &&
                      m.Parameters[0].RefKind == RefKind.Ref &&
                      m.Parameters[0].Type.Name == "BinaryStream" &&
                      SymbolEqualityComparer.Default.Equals(m.ReturnType, nestedType));

        if (!hasStaticRead)
        {
            var hasParameterlessCtor = nestedType.InstanceConstructors.Any(c => c.Parameters.Length == 0);
            var hasDecode = nestedType.GetMembers("Decode").OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.Parameters.Length == 1 &&
                          m.Parameters[0].RefKind == RefKind.Ref &&
                          m.Parameters[0].Type.Name == "BinaryStream");

            if (!hasParameterlessCtor || !hasDecode)
            {
                result.Diagnostics.Add(Diagnostic.Create(Diagnostics.NestedMissingRead,
                    location, nestedType.ToDisplayString(), classDisplayName, propertyName));
                return false;
            }
        }

        var hasWrite = nestedType.GetMembers("Write").OfType<IMethodSymbol>()
            .Any(m => !m.IsStatic && m.Parameters.Length == 1 &&
                      m.Parameters[0].RefKind == RefKind.Ref &&
                      m.Parameters[0].Type.Name == "BinaryStream");

        if (!hasWrite)
        {
            result.Diagnostics.Add(Diagnostic.Create(Diagnostics.NestedMissingWrite,
                location, nestedType.ToDisplayString(), classDisplayName, propertyName));
            return false;
        }

        return true;
    }

    private static bool IsSupportedNumericOrBool(SpecialType type, WireKind kind)
    {
        if (kind == WireKind.Wire)
        {
            return type is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_Int16
                or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single
                or SpecialType.System_Double;
        }

        return type is SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

    private static string FormatConstant(TypedConstant tc)
    {
        if (tc.IsNull) return "null";
        if (tc.Type?.TypeKind == TypeKind.Enum)
            return $"({tc.Type.ToDisplayString()}){tc.Value}";

        return tc.Value switch
        {
            bool b => b ? "true" : "false",
            string s => SymbolDisplay.FormatLiteral(s, true),
            _ => tc.Value?.ToString() ?? "default"
        };
    }

    private static string SourceText(PacketModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by zenith.PacketGenerator from [GamePacket] + [Wire*] attributes. See docs/decisions.md §76.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Zenith.Raknet.Stream;");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }
        sb.AppendLine($"partial class {model.ClassName}");
        sb.AppendLine("{");
        sb.AppendLine($"    public override int Id => {model.ProtocolId};");
        sb.AppendLine();
        sb.AppendLine("    public override Span<byte> Encode()");
        sb.AppendLine("    {");
        sb.AppendLine("        var writer = new BinaryStream();");
        sb.AppendLine("        writer.WriteUnsignedVarInt(Id);");
        foreach (var field in model.Fields)
            AppendIndented(sb, BuildEncodeStatement(field), 2);
        sb.AppendLine("        return writer.GetBufferDisposing();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public override void Decode(ref BinaryStream stream)");
        sb.AppendLine("    {");
        foreach (var field in model.Fields)
            AppendIndented(sb, BuildDecodeStatement(field), 2);
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendIndented(StringBuilder sb, string statement, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 4);
        foreach (var line in statement.Split('\n'))
            sb.AppendLine(indent + line.TrimEnd('\r'));
    }

    private static string BuildEncodeStatement(FieldModel f)
    {
        var core = BuildEncodeCore(f);
        var stmt = core;

        if (f.IsOptional)
        {
            stmt = $"if ({f.PropertyName} is not null)\n{{\n    writer.WriteBool(true);\n    {core}\n}}\nelse\n{{\n    writer.WriteBool(false);\n}}";
        }

        if (f.WhenOtherProperty is not null)
        {
            stmt = $"if ({f.WhenOtherProperty} == {f.WhenValueLiteral})\n{{\n    {Indent(stmt)}\n}}";
        }

        return stmt;
    }

    private static string BuildDecodeStatement(FieldModel f)
    {
        string stmt;

        if (f.Kind == WireKind.WireNestedArray)
        {
            stmt = BuildNestedArrayDecode(f);
        }
        else if (f.IsOptional)
        {
            var valueExpr = BuildDecodeValueExpr(f);
            stmt = $"{f.PropertyName} = stream.ReadBool() ? ({f.ClrTypeName})({valueExpr}) : null;";
        }
        else if (f.Kind == WireKind.WireNested && !f.NestedHasStaticRead)
        {
            stmt = $"{{\n    var __v = new {f.NestedTypeName}();\n    __v.Decode(ref stream);\n    {f.PropertyName} = __v;\n}}";
        }
        else
        {
            stmt = $"{f.PropertyName} = {BuildDecodeValueExpr(f)};";
        }

        if (f.WhenOtherProperty is not null)
        {
            stmt = $"if ({f.WhenOtherProperty} == {f.WhenValueLiteral})\n{{\n    {Indent(stmt)}\n}}";
        }

        return stmt;
    }

    private static string Indent(string s) => s.Replace("\n", "\n    ");

    private static string EndianArg(FieldModel f, bool leadingComma) =>
        leadingComma ? $", BinaryStream.Endianess.{f.Endianess}" : $"BinaryStream.Endianess.{f.Endianess}";

    private static (string Suffix, bool HasEndian) WireMethod(FieldModel f)
    {
        var typeName = f.EnumUnderlyingTypeName ?? f.ClrTypeName;
        if (f.IsValueTypeNullable) typeName = typeName.TrimEnd('?');
        return WireTypeVocabulary.FixedSuffix(typeName);
    }

    /// <summary>[WireVar(unsigned: true)] on an int/long property forces UnsignedVarInt/
    /// UnsignedVarLong directly (BinaryStream's Unsigned* methods already take/return signed
    /// int/long, so no cast is needed) instead of the default zigzag VarInt/VarLong - see
    /// WireVarAttribute's doc comment for why this can't be inferred from CLR type alone.</summary>
    private static (string MethodSuffix, string? StorageCastType) ResolveVarMethod(string underlying, bool isUnsignedVar)
    {
        if (isUnsignedVar)
        {
            return underlying switch
            {
                "int" => ("UnsignedVarInt", null),
                "long" => ("UnsignedVarLong", null),
                _ => WireTypeVocabulary.VarInfo(underlying)
            };
        }

        return WireTypeVocabulary.VarInfo(underlying);
    }

    private static string BuildEncodeCore(FieldModel f)
    {
        var valueExpr = f.IsOptional && f.IsValueTypeNullable ? $"{f.PropertyName}.Value" : f.PropertyName;

        switch (f.Kind)
        {
            case WireKind.Wire:
            {
                var (suffix, hasEndian) = WireMethod(f);
                var castExpr = f.IsEnum ? $"({f.EnumUnderlyingTypeName}){valueExpr}" : valueExpr;
                return hasEndian
                    ? $"writer.Write{suffix}({castExpr}, BinaryStream.Endianess.{f.Endianess});"
                    : $"writer.Write{suffix}({castExpr});";
            }
            case WireKind.WireVar:
            {
                var underlying = f.EnumUnderlyingTypeName ?? f.ClrTypeName;
                var castExpr = f.IsEnum ? $"({underlying}){valueExpr}" : valueExpr;
                var (methodSuffix, storageCast) = ResolveVarMethod(underlying, f.IsUnsignedVar);
                var argExpr = storageCast is null ? castExpr : $"({storageCast}){castExpr}";
                return $"writer.Write{methodSuffix}({argExpr});";
            }
            case WireKind.WireString:
                return f.Utf16LengthPrefixed
                    ? $"writer.WriteString({valueExpr});"
                    : $"writer.WriteVarString({valueExpr});";
            case WireKind.WireByteArray:
                return $"writer.WriteByteArray({valueExpr});";
            case WireKind.WireUuid:
                return $"writer.WriteUuid({valueExpr});";
            case WireKind.WireNested:
                return $"{valueExpr}.Write(ref writer);";
            case WireKind.WireNestedArray:
                return BuildNestedArrayEncode(f);
            default:
                throw new InvalidOperationException();
        }
    }

    private static string BuildDecodeValueExpr(FieldModel f)
    {
        switch (f.Kind)
        {
            case WireKind.Wire:
            {
                var (suffix, hasEndian) = WireMethod(f);
                var readExpr = hasEndian ? $"stream.Read{suffix}(BinaryStream.Endianess.{f.Endianess})" : $"stream.Read{suffix}()";
                return f.IsEnum ? $"({f.ClrTypeName.TrimEnd('?')}){readExpr}" : readExpr;
            }
            case WireKind.WireVar:
            {
                var underlying = f.EnumUnderlyingTypeName ?? f.ClrTypeName.TrimEnd('?');
                var (methodSuffix, storageCast) = ResolveVarMethod(underlying, f.IsUnsignedVar);
                var readExpr = storageCast is null
                    ? $"stream.Read{methodSuffix}()"
                    : $"({underlying})stream.Read{methodSuffix}()";
                return f.IsEnum ? $"({f.ClrTypeName.TrimEnd('?')}){readExpr}" : readExpr;
            }
            case WireKind.WireString:
                return f.Utf16LengthPrefixed ? "stream.ReadString()" : "stream.ReadVarString()";
            case WireKind.WireByteArray:
                return "stream.ReadByteArray()";
            case WireKind.WireUuid:
                return "stream.ReadUuid()";
            case WireKind.WireNested:
                return $"{f.NestedTypeName}.Read(ref stream)";
            default:
                throw new InvalidOperationException();
        }
    }

    private static string BuildNestedArrayEncode(FieldModel f)
    {
        var countStmt = f.CountEncoding switch
        {
            "FixedByte" => $"writer.WriteByte((byte){f.PropertyName}.Length);",
            "FixedUShort" => $"writer.WriteUShort((ushort){f.PropertyName}.Length, BinaryStream.Endianess.{f.CountEndianess});",
            "FixedUInt" => $"writer.WriteUInt((uint){f.PropertyName}.Length, BinaryStream.Endianess.{f.CountEndianess});",
            _ => $"writer.WriteUnsignedVarInt({f.PropertyName}.Length);"
        };

        return $"{countStmt}\nforeach (var __item in {f.PropertyName})\n{{\n    __item.Write(ref writer);\n}}";
    }

    private static string BuildNestedArrayDecode(FieldModel f)
    {
        var countExpr = f.CountEncoding switch
        {
            "FixedByte" => "stream.ReadByte()",
            "FixedUShort" => $"stream.ReadUShort(BinaryStream.Endianess.{f.CountEndianess})",
            "FixedUInt" => $"(int)stream.ReadUInt(BinaryStream.Endianess.{f.CountEndianess})",
            _ => "stream.ReadUnsignedVarInt()"
        };

        var readItem = f.NestedHasStaticRead
            ? $"__arr[__i] = {f.NestedTypeName}.Read(ref stream);"
            : $"var __item = new {f.NestedTypeName}();\n    __item.Decode(ref stream);\n    __arr[__i] = __item;";

        return $"{{\n    var __count = {countExpr};\n    var __arr = new {f.NestedTypeName}[__count];\n    for (var __i = 0; __i < __count; __i++)\n    {{\n        {readItem}\n    }}\n    {f.PropertyName} = __arr;\n}}";
    }
}

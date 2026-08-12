using Xunit;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public class MojangSchemaSourceTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly string _mojangDir;
    private readonly MojangSchemaSource _source = new();

    public MojangSchemaSourceTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "protocol-import-tests-" + Guid.NewGuid());
        _mojangDir = Path.Combine(_cacheDir, "mojang");
        Directory.CreateDirectory(_mojangDir);
    }

    public void Dispose()
    {
        _source.Dispose();
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }

    private void WriteFile(string name, string json) =>
        File.WriteAllText(Path.Combine(_mojangDir, $"{name}.json"), json);

    [Fact]
    public void Fields_are_sorted_by_ordinal_index_not_declaration_order()
    {
        // Mirrors the real CommandRequestPacketPayload.json: declared Command, IsInternal,
        // Origin, Version but ordinal indices are 0, 2, 1, 3 - Origin is actually field #1.
        WriteFile("Pkt", """
            {"$ref": "./PktPayload.json", "$metaProperties": {"[cereal:packet]": 77}}
            """);
        WriteFile("PktPayload", """
            {
                "type": "object",
                "properties": {
                    "Command": {"type": "string", "x-ordinal-index": 0},
                    "IsInternal": {"type": "boolean", "x-ordinal-index": 2},
                    "Origin": {"type": "string", "x-ordinal-index": 1},
                    "Version": {"type": "string", "x-ordinal-index": 3}
                },
                "required": ["Command", "IsInternal", "Origin", "Version"]
            }
            """);

        var packet = _source.ReadPacket(_cacheDir, "Pkt");

        Assert.NotNull(packet);
        Assert.Equal(77, packet!.Id);
        Assert.Equal(["Command", "Origin", "IsInternal", "Version"], packet.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Payload_level_required_absence_means_optional()
    {
        WriteFile("Pkt", """{"$ref": "./PktPayload.json", "$metaProperties": {"[cereal:packet]": 1}}""");
        WriteFile("PktPayload", """
            {
                "type": "object",
                "properties": {
                    "Always": {"type": "string", "x-ordinal-index": 0},
                    "Sometimes": {"type": "string", "x-ordinal-index": 1}
                },
                "required": ["Always"]
            }
            """);

        var packet = _source.ReadPacket(_cacheDir, "Pkt")!;

        Assert.False(packet.Fields.Single(f => f.Name == "Always").Optional);
        Assert.True(packet.Fields.Single(f => f.Name == "Sometimes").Optional);
    }

    [Fact]
    public void Type_level_required_absence_does_not_mean_optional()
    {
        // Mirrors CommandOriginData.json: PlayerId/RequestId are absent from "required" but the
        // real hand-written Read/Write always reads them unconditionally - "required" there means
        // "has a JSON-Schema default value", not "wire-optional". See ADR §76 addendum.
        WriteFile("SomeType", """
            {
                "type": "object",
                "properties": {
                    "AlwaysPresent": {"type": "string", "x-ordinal-index": 0},
                    "AlsoAlwaysPresent": {"type": "string", "default": "", "x-ordinal-index": 1}
                },
                "required": ["AlwaysPresent"]
            }
            """);

        var type = _source.ReadType(_cacheDir, "SomeType")!;

        Assert.All(type.Fields, f => Assert.False(f.Optional));
    }

    [Fact]
    public void Enum_ref_flattens_to_its_underlying_scalar()
    {
        WriteFile("Pkt", """{"$ref": "./PktPayload.json", "$metaProperties": {"[cereal:packet]": 1}}""");
        WriteFile("PktPayload", """
            {
                "type": "object",
                "properties": {
                    "Action": {"$ref": "./ActionEnum.json", "x-ordinal-index": 0}
                },
                "required": ["Action"]
            }
            """);
        WriteFile("ActionEnum", """
            {
                "type": "string",
                "enum": ["NoAction", "Swing"],
                "x-underlying-type": "uint8"
            }
            """);

        var packet = _source.ReadPacket(_cacheDir, "Pkt")!;

        Assert.Equal("uint8", packet.Fields.Single().Type);
    }

    [Fact]
    public void Single_scalar_id_wrapper_ref_flattens_to_wrapped_varint_type()
    {
        // Mirrors ActorRuntimeID.json: one property, uint64 + Compression.
        WriteFile("Pkt", """{"$ref": "./PktPayload.json", "$metaProperties": {"[cereal:packet]": 1}}""");
        WriteFile("PktPayload", """
            {
                "type": "object",
                "properties": {
                    "RuntimeId": {"$ref": "./ActorRuntimeID.json", "x-ordinal-index": 0}
                },
                "required": ["RuntimeId"]
            }
            """);
        WriteFile("ActorRuntimeID", """
            {
                "type": "object",
                "properties": {
                    "Actor Runtime ID": {
                        "type": "integer",
                        "x-underlying-type": "uint64",
                        "x-serialization-options": ["Compression"]
                    }
                }
            }
            """);

        var packet = _source.ReadPacket(_cacheDir, "Pkt")!;

        Assert.Equal("uvarint64", packet.Fields.Single().Type);
    }

    [Fact]
    public void Wrapper_around_another_wrapper_ref_is_chased_recursively()
    {
        // Hypothetical but plausible shape: a wrapper whose lone property is itself a $ref to
        // another wrapper, rather than a plain scalar directly. Must still flatten instead of
        // being (wrongly) treated as a genuine nested type.
        WriteFile("Pkt", """{"$ref": "./PktPayload.json", "$metaProperties": {"[cereal:packet]": 1}}""");
        WriteFile("PktPayload", """
            {
                "type": "object",
                "properties": {
                    "Id": {"$ref": "./OuterWrapper.json", "x-ordinal-index": 0}
                },
                "required": ["Id"]
            }
            """);
        WriteFile("OuterWrapper", """
            {
                "type": "object",
                "properties": {
                    "Inner": {"$ref": "./InnerWrapper.json"}
                }
            }
            """);
        WriteFile("InnerWrapper", """
            {
                "type": "object",
                "properties": {
                    "Value": {
                        "type": "integer",
                        "x-underlying-type": "uint32",
                        "x-serialization-options": ["Compression"]
                    }
                }
            }
            """);

        var packet = _source.ReadPacket(_cacheDir, "Pkt")!;

        Assert.Equal("uvarint32", packet.Fields.Single().Type);
    }

    [Fact]
    public void Array_field_produces_non_null_repeat_prefix_sentinel()
    {
        WriteFile("Pkt", """{"$ref": "./PktPayload.json", "$metaProperties": {"[cereal:packet]": 1}}""");
        WriteFile("PktPayload", """
            {
                "type": "object",
                "properties": {
                    "Ids": {
                        "type": "array",
                        "items": {"$ref": "./mce__UUID.json"},
                        "x-ordinal-index": 0
                    }
                },
                "required": ["Ids"]
            }
            """);

        var packet = _source.ReadPacket(_cacheDir, "Pkt")!;
        var field = packet.Fields.Single();

        Assert.NotNull(field.RepeatPrefix);
        Assert.Equal("mce::uuid", field.Type);
        Assert.Equal(SchemaConstruct.Array, field.Construct);
        Assert.Equal("./mce__UUID.json", field.Reference);
    }

    [Fact]
    public void Union_is_preserved_with_an_explicit_unsupported_reason()
    {
        WriteFile("Pkt", """{"$ref": "./PktPayload.json", "$metaProperties": {"[cereal:packet]": 1}}""");
        WriteFile("PktPayload", """
            { "type": "object", "properties": {
                "Entry": {"oneOf": [{"$ref": "./Add.json"}, {"$ref": "./Remove.json"}], "x-ordinal-index": 0}
            }, "required": ["Entry"] }
            """);

        var field = _source.ReadPacket(_cacheDir, "Pkt")!.Fields.Single();

        Assert.Equal(SchemaConstruct.Union, field.Construct);
        Assert.True(field.IsComplexType);
        Assert.Equal("union discriminator not supported", field.UnsupportedReason);
        Assert.Equal("", field.Type);
    }
}

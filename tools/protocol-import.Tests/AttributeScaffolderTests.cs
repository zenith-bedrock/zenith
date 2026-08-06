using Xunit;
using Zenith.ProtocolImport.Scaffolding;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public class AttributeScaffolderTests
{
    [Fact]
    public void Flat_packet_scaffolds_matching_wire_attributes()
    {
        var packet = new PacketSchema(10, "SetTimePacket",
        [
            new FieldSchema("Time", "varint32", null, false, null)
        ]);

        var scaffolder = new AttributeScaffolder(new FakeSchemaSource(), ".cache");
        var result = scaffolder.Scaffold(packet);

        Assert.Contains("[GamePacket(10)]", result.SourceText);
        Assert.Contains("sealed partial class SetTimePacket : DataPacket", result.SourceText);
        Assert.Contains("[WireVar]", result.SourceText);
        Assert.Contains("public int Time { get; set; }", result.SourceText);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Optional_field_gets_nullable_type_and_WireOptional()
    {
        var packet = new PacketSchema(1, "SomePacket",
        [
            new FieldSchema("Swing Source", "string", null, true, null)
        ]);

        var scaffolder = new AttributeScaffolder(new FakeSchemaSource(), ".cache");
        var result = scaffolder.Scaffold(packet);

        Assert.Contains("[WireString]", result.SourceText);
        Assert.Contains("[WireOptional]", result.SourceText);
        Assert.Contains("public string? SwingSource { get; set; }", result.SourceText);
    }

    [Fact]
    public void Unknown_type_emits_todo_comment_and_note()
    {
        var packet = new PacketSchema(1, "SomePacket",
        [
            new FieldSchema("Weird Field", "SomeUnresolvedType", null, false, null)
        ]);

        var scaffolder = new AttributeScaffolder(new FakeSchemaSource(), ".cache");
        var result = scaffolder.Scaffold(packet);

        Assert.Contains("// TODO: resolve type 'SomeUnresolvedType' by hand", result.SourceText);
        Assert.Single(result.Notes);
    }

    [Fact]
    public void Resolvable_nested_type_scaffolds_struct_with_read_write()
    {
        var source = new FakeSchemaSource();
        source.Types["Origin"] = new TypeSchema("Origin",
        [
            new FieldSchema("Value", "string", null, false, null)
        ]);

        var packet = new PacketSchema(1, "SomePacket",
        [
            new FieldSchema("Origin", "Origin", null, false, null)
        ]);

        var scaffolder = new AttributeScaffolder(source, ".cache");
        var result = scaffolder.Scaffold(packet);

        Assert.Contains("readonly struct Origin", result.SourceText);
        Assert.Contains("public static Origin Read(ref BinaryStream stream)", result.SourceText);
        Assert.Contains("public void Write(ref BinaryStream writer)", result.SourceText);
        Assert.Contains("[WireNested]", result.SourceText);
        Assert.Single(result.Notes);
    }

    [Fact]
    public void Primitive_array_emits_todo_not_WireNestedArray()
    {
        var packet = new PacketSchema(1, "SomePacket",
        [
            new FieldSchema("Ids", "mce::uuid", null, false, "uvarint32")
        ]);

        var scaffolder = new AttributeScaffolder(new FakeSchemaSource(), ".cache");
        var result = scaffolder.Scaffold(packet);

        Assert.DoesNotContain("[WireNestedArray(", result.SourceText);
        Assert.Contains("// TODO: primitive-element array", result.SourceText);
        Assert.Single(result.Notes);
    }

    [Fact]
    public void Resolvable_nested_array_emits_WireNestedArray()
    {
        var source = new FakeSchemaSource();
        source.Types["Item"] = new TypeSchema("Item",
        [
            new FieldSchema("Id", "varint32", null, false, null)
        ]);

        var packet = new PacketSchema(1, "SomePacket",
        [
            new FieldSchema("Items", "Item", null, false, "uvarint32")
        ]);

        var scaffolder = new AttributeScaffolder(source, ".cache");
        var result = scaffolder.Scaffold(packet);

        Assert.Contains("[WireNestedArray(CountEncoding.UnsignedVarInt)]", result.SourceText);
        Assert.Contains("public Item[] Items { get; set; } = [];", result.SourceText);
    }
}

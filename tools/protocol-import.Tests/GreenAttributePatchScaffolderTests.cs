using Xunit;
using Zenith.ProtocolImport.Scaffolding;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public sealed class GreenAttributePatchScaffolderTests
{
    [Fact]
    public void Scalar_known_field_scaffolds_a_partial_wire_attribute_patch()
    {
        var result = GreenAttributePatchScaffolder.TryScaffold(
            new PacketSchema(1, "SetTimePacket", []),
            new FieldSchema("Time", "varint32", null, false, null), out var source, out var reason);

        Assert.True(result);
        Assert.Empty(reason);
        Assert.Contains("partial class SetTimePacket", source);
        Assert.Contains("[WireVar]", source);
        Assert.Contains("public int Time", source);
    }

    [Fact]
    public void Array_is_not_automated_even_when_its_element_type_is_known()
    {
        var result = GreenAttributePatchScaffolder.TryScaffold(
            new PacketSchema(1, "SomePacket", []),
            new FieldSchema("Values", "int32", null, false, "uvarint32", Construct: SchemaConstruct.Array),
            out _, out var reason);

        Assert.False(result);
        Assert.Contains("scalar", reason);
    }
}

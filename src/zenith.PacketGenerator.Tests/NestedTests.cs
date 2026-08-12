using Xunit;

namespace Zenith.PacketGenerator.Tests;

public class NestedTests
{
    [Fact]
    public void Uuid_array_generates_count_prefixed_uuid_loop()
    {
        const string source = """
            using System;
            using Zenith.Packets.Generation;
            using Zenith.Raknet.Stream;
            namespace Zenith.Packets;
            [GamePacket(2)] sealed partial class ArrayPacket : DataPacket
            { [WireUuidArray] public Guid[] Ids { get; set; } = []; }
            """;
        var (generated, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Empty(diagnostics);
        var text = Assert.Single(generated);
        Assert.Contains("writer.WriteUnsignedVarInt(Ids.Length);", text);
        Assert.Contains("writer.WriteUuid(__item);", text);
        Assert.Contains("__arr[__i] = stream.ReadUuid();", text);
    }

    [Fact]
    public void Nested_single_object_uses_static_read_factory()
    {
        const string source = """
            using Zenith.Packets.Generation;
            using Zenith.Raknet.Stream;
            namespace Zenith.Packets;

            readonly struct Origin
            {
                public string Value { get; init; }
                public static Origin Read(ref BinaryStream stream) => new() { Value = stream.ReadVarString() };
                public void Write(ref BinaryStream writer) => writer.WriteVarString(Value);
            }

            [GamePacket(77)]
            sealed partial class CommandRequestPacket : DataPacket
            {
                [WireNested]
                public Origin Origin { get; set; }
            }
            """;

        var (generated, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.Empty(diagnostics);
        var text = Assert.Single(generated);
        Assert.Contains("Origin.Write(ref writer);", text);
        Assert.Contains("Origin = Zenith.Packets.Origin.Read(ref stream);", text);
    }

    [Fact]
    public void Nested_object_missing_read_shape_reports_ZPG006()
    {
        const string source = """
            using Zenith.Packets.Generation;
            using Zenith.Raknet.Stream;
            namespace Zenith.Packets;

            readonly struct Broken
            {
                public void Write(ref BinaryStream writer) { }
            }

            [GamePacket(1)]
            sealed partial class BrokenPacket : DataPacket
            {
                [WireNested]
                public Broken Field { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.Contains(diagnostics, d => d.Id == "ZPG006");
    }

    [Fact]
    public void Nested_array_generates_count_prefixed_loop()
    {
        const string source = """
            using Zenith.Packets.Generation;
            using Zenith.Raknet.Stream;
            namespace Zenith.Packets;

            readonly struct Item
            {
                public static Item Read(ref BinaryStream stream) => default;
                public void Write(ref BinaryStream writer) { }
            }

            [GamePacket(2)]
            sealed partial class ArrayPacket : DataPacket
            {
                [WireNestedArray]
                public Item[] Items { get; set; } = [];
            }
            """;

        var (generated, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.Empty(diagnostics);
        var text = Assert.Single(generated);
        Assert.Contains("writer.WriteUnsignedVarInt(Items.Length);", text);
        Assert.Contains("foreach (var __item in Items)", text);
        Assert.Contains("var __count = stream.ReadUnsignedVarInt();", text);
        Assert.Contains("Items = __arr;", text);
    }
}

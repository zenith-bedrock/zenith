using Xunit;
using Zenith.Nbt;
using Zenith.Packets;
using Zenith.Raknet.Stream;

namespace Zenith.Tests;

public class ItemRegistryPacketTests
{
    [Fact]
    public void Encode_preserves_component_definitions_between_empty_registry_entries()
    {
        var definitions = new NbtCompound();
        definitions.Set("minecraft:icon", NbtTag.String("test-model"));
        var components = new NbtCompound();
        components.Set("components", NbtTag.Compound(definitions));
        var packet = new ItemRegistryPacket
        {
            Entries = [new("minecraft:stone", 1, 2, false),
                new("minecraft:test", 300, 1, true, components),
                new("minecraft:air", -158, 2, false)]
        };
        var stream = new BinaryStream(packet.Encode().ToArray());
        Assert.Equal(162, (int)stream.ReadUnsignedVarInt());
        Assert.Equal(3, (int)stream.ReadUnsignedVarInt());
        for (var i = 0; i < packet.Entries.Count; i++)
        {
            var expected = packet.Entries[i];
            Assert.Equal(expected.Name, stream.ReadVarString());
            Assert.Equal(expected.NetworkId, stream.ReadShort(BinaryStream.Endianess.Little));
            Assert.Equal(expected.ComponentBased, stream.ReadBool());
            Assert.Equal(expected.Version, stream.ReadVarInt());
            var decoded = NbtCodec.Decode(stream.Buffer.AsSpan(stream.Offset, stream.Length - stream.Offset), NbtEncoding.Network);
            stream.Offset += decoded.BytesConsumed;
            Assert.True(decoded.Root.Tag.TryGetCompound(out var root));
            if (i == 1)
            {
                Assert.True(root.TryGet("components", out var tag));
                Assert.True(tag.TryGetCompound(out var model));
                Assert.True(model.TryGet("minecraft:icon", out var icon));
                Assert.True(icon.TryGetString(out var value));
                Assert.Equal("test-model", value);
            }
            else
                Assert.Equal(0, root.Count);
        }
        Assert.True(stream.IsEndOfFile);
    }
}

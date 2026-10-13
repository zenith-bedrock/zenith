using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Byte-level check for <c>NetworkItemStack.WriteNetworkItemStackDescriptor</c> (ItemV4 shape,
/// protocol 2168+ — ADR §87). Found via zenith-smoke-bot: InventoryContentPacket failed to
/// decode ("offset out of range") whenever a slot carried a non-zero stack net id, because this
/// writer had an extra "stack_id_variant" tag byte the real wire shape doesn't have.
/// </summary>
public class NetworkItemStackDescriptorTests
{
    [Fact]
    public void CreativeContent_anonymous_group_keeps_the_following_item_aligned()
    {
        var packet = new CreativeContentPacket
        {
            Groups = [new CreativeGroupEntry(1, "", NetworkItemStack.Empty)],
            Items = [new CreativeItemEntry(7, new NetworkItemStack(5, 1, 7), 0)]
        };

        // Protocol 2193: header; group count/category/name; complete air icon;
        // item count/net id; item descriptor with ten-byte user data; group index.
        Assert.Equal(new byte[]
        {
            0x91, 0x01, 1, 1, 0,
            0, 0, 0, 0, 0, 0,
            1, 7, 10, 1, 0, 0, 14, 10,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        }, packet.Encode().ToArray());
    }

    [Fact]
    public void WriteNetworkItemStackDescriptor_with_stack_net_id_has_no_extra_tag_byte()
    {
        var stack = new NetworkItemStack(NetworkId: 5, Count: 4, BlockRuntimeId: 0, Meta: 0, StackNetworkId: 3);
        var writer = new BinaryStream();
        stack.WriteNetworkItemStackDescriptor(ref writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal(5, stream.ReadShort(BinaryStream.Endianess.Little));
        Assert.Equal((ushort)4, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // meta
        Assert.True(stream.ReadBool()); // has_stack_id
        Assert.Equal(3, stream.ReadVarInt()); // stack_id — no tag byte in between
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // block_runtime_id
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // extra blob length
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void WriteNetworkItemStackDescriptor_without_stack_net_id_skips_the_id_field()
    {
        var stack = new NetworkItemStack(NetworkId: 5, Count: 4, BlockRuntimeId: 7);
        var writer = new BinaryStream();
        stack.WriteNetworkItemStackDescriptor(ref writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        _ = stream.ReadShort(BinaryStream.Endianess.Little);
        _ = stream.ReadUShort(BinaryStream.Endianess.Little);
        _ = stream.ReadUnsignedVarInt();
        Assert.False(stream.ReadBool()); // has_stack_id
        Assert.Equal(7, (int)stream.ReadUnsignedVarInt()); // block_runtime_id right after the bool
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt());
        Assert.True(stream.IsEndOfFile);
    }
}

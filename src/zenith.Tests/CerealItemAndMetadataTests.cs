using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Byte-level checks for the protocol 2168+ (ADR §82) shapes this session added: the entity
/// metadata double-type-write and NetworkItemStack.WriteSerializedNetworkItemStackDescriptor.
/// No live client to round-trip against, so these decode the exact wire layout by hand and
/// assert full stream consumption (catches under/over-run, not just "didn't throw").
/// </summary>
public class CerealItemAndMetadataTests
{
    [Fact]
    public void WriteSerializedNetworkItemStackDescriptor_encodes_all_fields_unconditionally()
    {
        var stack = new NetworkItemStack(NetworkId: 355, Count: 3, BlockRuntimeId: 0, Meta: 5, StackNetworkId: 0);
        var writer = new BinaryStream();
        stack.WriteSerializedNetworkItemStackDescriptor(ref writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal(355, stream.ReadShort(BinaryStream.Endianess.Little));
        Assert.Equal((ushort)3, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal(5, stream.ReadUnsignedVarInt()); // aux_value
        Assert.False(stream.ReadBool()); // net_id_variant has-flag
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // block_runtime_id
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // user_data_buffer length
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void WriteSerializedNetworkItemStackDescriptor_air_item_still_writes_every_field()
    {
        // Unlike the legacy ItemStackWrapper/ItemStack writers, the cereal-bound
        // SerializedNetworkItemStackDescriptor has no air-item early-out (ADR §82).
        var air = NetworkItemStack.Empty;
        var writer = new BinaryStream();
        air.WriteSerializedNetworkItemStackDescriptor(ref writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal(0, stream.ReadShort(BinaryStream.Endianess.Little));
        Assert.Equal((ushort)0, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal(0, stream.ReadUnsignedVarInt());
        Assert.False(stream.ReadBool());
        Assert.Equal(0, stream.ReadUnsignedVarInt());
        Assert.Equal(0, stream.ReadUnsignedVarInt());
        Assert.True(stream.IsEndOfFile);
        // 2 (short) + 2 (ushort) + 1 (aux=0) + 1 (bool) + 1 (blockRuntimeId=0) + 1 (userdata len=0)
        Assert.Equal(8, bytes.Length);
    }

    [Fact]
    public void WriteSerializedNetworkItemStackDescriptor_with_stack_net_id_writes_signed_varint_no_tag_byte()
    {
        // ADR §82 / EndstoneMC bedrock-protocol note: the net-id-variant tag byte is gone at
        // 2168+ (sign/parity encode the case) — a present id is just a bare signed varint.
        var stack = new NetworkItemStack(NetworkId: 10, Count: 1, BlockRuntimeId: 2, Meta: 0, StackNetworkId: 77);
        var writer = new BinaryStream();
        stack.WriteSerializedNetworkItemStackDescriptor(ref writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        _ = stream.ReadShort(BinaryStream.Endianess.Little);
        _ = stream.ReadUShort(BinaryStream.Endianess.Little);
        _ = stream.ReadUnsignedVarInt();
        Assert.True(stream.ReadBool());
        Assert.Equal(77, stream.ReadVarInt()); // no intervening tag varint
        Assert.Equal(2, stream.ReadUnsignedVarInt());
        Assert.Equal(0, stream.ReadUnsignedVarInt());
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void WriteVisibleNameMetadata_writes_each_entry_type_twice()
    {
        var writer = new BinaryStream();
        EntityMetadataWriter.WriteVisibleNameMetadata(ref writer, "steve", sneaking: true, sprinting: false);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        var count = stream.ReadUnsignedVarInt();
        Assert.Equal(9, count);

        AssertEntry(ref stream, EntityMetaKey.Flags, EntityMetaType.Long);
        var flags = stream.ReadVarLong();
        Assert.NotEqual(0L, flags & EntityFlag.Bit(EntityFlag.Sneaking));

        AssertEntry(ref stream, EntityMetaKey.ColorIndex, EntityMetaType.Byte);
        _ = stream.ReadByte();

        AssertEntry(ref stream, EntityMetaKey.Name, EntityMetaType.String);
        Assert.Equal("steve", stream.ReadVarString());

        AssertEntry(ref stream, EntityMetaKey.EffectColor, EntityMetaType.Int);
        _ = stream.ReadVarInt();

        AssertEntry(ref stream, EntityMetaKey.EffectAmbience, EntityMetaType.Byte);
        _ = stream.ReadByte();

        AssertEntry(ref stream, EntityMetaKey.Scale, EntityMetaType.Float);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        AssertEntry(ref stream, EntityMetaKey.Width, EntityMetaType.Float);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        AssertEntry(ref stream, EntityMetaKey.Height, EntityMetaType.Float);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        AssertEntry(ref stream, EntityMetaKey.AlwaysShowNameTag, EntityMetaType.Byte);
        _ = stream.ReadByte();

        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void WriteFlagsOnly_single_entry_also_writes_type_twice()
    {
        var writer = new BinaryStream();
        EntityMetadataWriter.WriteFlagsOnly(ref writer, 42L);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal(1, stream.ReadUnsignedVarInt());
        AssertEntry(ref stream, EntityMetaKey.Flags, EntityMetaType.Long);
        Assert.Equal(42L, stream.ReadVarLong());
        Assert.True(stream.IsEndOfFile);
    }

    private static void AssertEntry(ref BinaryStream stream, int key, int type)
    {
        Assert.Equal(key, stream.ReadUnsignedVarInt());
        var discriminator = stream.ReadUnsignedVarInt();
        var payloadType = stream.ReadUnsignedVarInt();
        Assert.Equal(type, discriminator);
        Assert.Equal(type, payloadType);
    }
}

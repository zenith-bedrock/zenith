using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class EntityMetadataTests
{
    [Fact]
    public void BuildSpawnFlags_includes_sneaking_and_sprinting_bits()
    {
        var baseFlags = EntityMetadataWriter.BuildSpawnFlags();
        var sneak = EntityMetadataWriter.BuildSpawnFlags(sneaking: true);
        var sprint = EntityMetadataWriter.BuildSpawnFlags(sprinting: true);

        Assert.Equal(0, baseFlags & EntityFlag.Bit(EntityFlag.Sneaking));
        Assert.Equal(0, baseFlags & EntityFlag.Bit(EntityFlag.Sprinting));
        Assert.Equal(EntityFlag.Bit(EntityFlag.Sneaking), sneak & EntityFlag.Bit(EntityFlag.Sneaking));
        Assert.Equal(EntityFlag.Bit(EntityFlag.Sprinting), sprint & EntityFlag.Bit(EntityFlag.Sprinting));
        Assert.Equal(EntityFlag.Bit(EntityFlag.Breathing), baseFlags & EntityFlag.Bit(EntityFlag.Breathing));
    }

    /// <summary>
    /// Phase XXIII-B — the exact bug class this builder was introduced to make structurally
    /// impossible: a hand-typed leading entry count that drifted from how many entries actually
    /// followed, found via a real client crash/disconnect. The count must always equal the number of
    /// builder calls made, for any combination and count of entries — not just the specific 3-vs-4
    /// case that shipped broken.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void EntityMetadataBuilder_declared_count_always_matches_entries_added(int entryCount)
    {
        var builder = new EntityMetadataBuilder();
        for (var i = 0; i < entryCount; i++)
            builder.Int(100 + i, i);

        var writer = new BinaryStream();
        builder.WriteTo(ref writer);
        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());

        var declaredCount = stream.ReadUnsignedVarInt();
        Assert.Equal(entryCount, declaredCount);

        for (var i = 0; i < entryCount; i++)
        {
            Assert.Equal(100 + i, stream.ReadUnsignedVarInt());
            Assert.Equal(EntityMetaType.Int, stream.ReadUnsignedVarInt());
            Assert.Equal(EntityMetaType.Int, stream.ReadUnsignedVarInt());
            Assert.Equal(i, stream.ReadVarInt());
        }
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void EntityMetadataBuilder_chains_mixed_entry_types_in_call_order()
    {
        var builder = new EntityMetadataBuilder()
            .Flags(7L)
            .Scale(2f)
            .Dimensions(0.6f, 1.9f)
            .Byte(EntityMetaKey.ColorIndex, 3)
            .String(EntityMetaKey.Name, "steve");

        var writer = new BinaryStream();
        builder.WriteTo(ref writer);
        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());

        Assert.Equal(6, stream.ReadUnsignedVarInt());

        Assert.Equal(EntityMetaKey.Flags, stream.ReadUnsignedVarInt());
        _ = stream.ReadUnsignedVarInt(); _ = stream.ReadUnsignedVarInt();
        Assert.Equal(7L, stream.ReadVarLong());

        Assert.Equal(EntityMetaKey.Scale, stream.ReadUnsignedVarInt());
        _ = stream.ReadUnsignedVarInt(); _ = stream.ReadUnsignedVarInt();
        Assert.Equal(2f, stream.ReadFloat(BinaryStream.Endianess.Little));

        Assert.Equal(EntityMetaKey.Width, stream.ReadUnsignedVarInt());
        _ = stream.ReadUnsignedVarInt(); _ = stream.ReadUnsignedVarInt();
        Assert.Equal(0.6f, stream.ReadFloat(BinaryStream.Endianess.Little));

        Assert.Equal(EntityMetaKey.Height, stream.ReadUnsignedVarInt());
        _ = stream.ReadUnsignedVarInt(); _ = stream.ReadUnsignedVarInt();
        Assert.Equal(1.9f, stream.ReadFloat(BinaryStream.Endianess.Little));

        Assert.Equal(EntityMetaKey.ColorIndex, stream.ReadUnsignedVarInt());
        _ = stream.ReadUnsignedVarInt(); _ = stream.ReadUnsignedVarInt();
        Assert.Equal(3, stream.ReadByte());

        Assert.Equal(EntityMetaKey.Name, stream.ReadUnsignedVarInt());
        _ = stream.ReadUnsignedVarInt(); _ = stream.ReadUnsignedVarInt();
        Assert.Equal("steve", stream.ReadVarString());

        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void SetActorData_flags_only_omits_name_string()
    {
        var full = new SetActorDataPacket
        {
            ActorRuntimeId = 2,
            Name = "sneaker",
            Sneaking = true
        }.Encode().ToArray();

        var flagsOnly = new SetActorDataPacket
        {
            ActorRuntimeId = 2,
            FlagsOnly = true,
            Sneaking = true
        }.Encode().ToArray();

        Assert.Contains("sneaker", System.Text.Encoding.UTF8.GetString(full));
        Assert.DoesNotContain("sneaker", System.Text.Encoding.UTF8.GetString(flagsOnly));
        Assert.True(flagsOnly.Length < full.Length);
    }

    [Fact]
    public void Animate_swing_arm_roundtrips_protocol_1001_shape()
    {
        var original = new AnimatePacket
        {
            Action = AnimatePacket.ActionSwingArm,
            ActorRuntimeId = 42,
            Data = 0f,
            SwingSource = null
        };
        var encoded = original.Encode().ToArray();
        Assert.Equal((int)ProtocolInfo.ANIMATE_PACKET, ReadPacketId(encoded));

        var stream = new BinaryStream(encoded);
        _ = stream.ReadUnsignedVarInt(); // id
        var decoded = new AnimatePacket();
        decoded.Decode(ref stream);
        Assert.Equal(AnimatePacket.ActionSwingArm, decoded.Action);
        Assert.Equal(42UL, decoded.ActorRuntimeId);
        Assert.Equal(0f, decoded.Data);
        Assert.Null(decoded.SwingSource);
    }

    [Fact]
    public void Animate_swing_arm_with_source_roundtrips()
    {
        var original = new AnimatePacket
        {
            Action = AnimatePacket.ActionSwingArm,
            ActorRuntimeId = 3,
            Data = 0f,
            SwingSource = "attack"
        };
        var stream = new BinaryStream(original.Encode().ToArray());
        _ = stream.ReadUnsignedVarInt();
        var decoded = new AnimatePacket();
        decoded.Decode(ref stream);
        Assert.Equal("attack", decoded.SwingSource);
    }

    private static int ReadPacketId(byte[] encoded)
    {
        var stream = new BinaryStream(encoded);
        return stream.ReadUnsignedVarInt();
    }
}

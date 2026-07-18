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
    public void InputBitsetTest_reads_sneak_and_sprint_edge_bits()
    {
        // 7 bits per byte; flags 8, 25, 26, 39 need indices up through 39/7 = 5.
        var bytes = new byte[6];
        SetFlag(bytes, PlayerAuthInputPacket.InputFlagSneaking);
        SetFlag(bytes, PlayerAuthInputPacket.InputFlagStartSprinting);
        SetFlag(bytes, PlayerAuthInputPacket.InputFlagStopSprinting);
        SetFlag(bytes, PlayerAuthInputPacket.InputFlagMissedSwing);

        Assert.True(PlayerAuthInputPacket.InputBitsetTest(bytes, PlayerAuthInputPacket.InputFlagSneaking));
        Assert.True(PlayerAuthInputPacket.InputBitsetTest(bytes, PlayerAuthInputPacket.InputFlagStartSprinting));
        Assert.True(PlayerAuthInputPacket.InputBitsetTest(bytes, PlayerAuthInputPacket.InputFlagStopSprinting));
        Assert.True(PlayerAuthInputPacket.InputBitsetTest(bytes, PlayerAuthInputPacket.InputFlagMissedSwing));
        Assert.False(PlayerAuthInputPacket.InputBitsetTest(bytes, PlayerAuthInputPacket.InputFlagPerformBlockActions));
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

    private static void SetFlag(byte[] bitset, int flag)
    {
        var idx = flag / 7;
        var pos = flag % 7;
        bitset[idx] |= (byte)(1 << pos);
        // Continuation bits for prior bytes so length is coherent for writers; test only uses Test.
        for (var i = 0; i < idx; i++)
            bitset[i] |= 0x80;
    }
}

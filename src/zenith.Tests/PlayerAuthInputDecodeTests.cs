using Zenith.Network.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class PlayerAuthInputDecodeTests
{
    [Fact]
    public void InputBitset_flag_35_perform_block_actions()
    {
        // Flag 35 → byte index 5, bit 0: encode continuation for bytes 0..4
        var bits = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        Assert.True(PlayerAuthInputPacket.InputBitsetTest(bits, PlayerAuthInputPacket.InputFlagPerformBlockActions));
        Assert.False(PlayerAuthInputPacket.InputBitsetTest(bits, PlayerAuthInputPacket.InputFlagPerformItemInteraction));
    }

    [Fact]
    public void Decode_predict_destroy_block_action()
    {
        var w = new BinaryStream();
        // pitch yaw pos
        for (var i = 0; i < 5; i++)
            w.WriteFloat(1.5f + i, BinaryStream.Endianess.Little);
        // move_vector + head_yaw
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        // bitset with only flag 35 set
        w.WriteByte(0x80);
        w.WriteByte(0x80);
        w.WriteByte(0x80);
        w.WriteByte(0x80);
        w.WriteByte(0x80);
        w.WriteByte(0x01);
        // modes
        w.WriteUnsignedVarInt(1);
        w.WriteUnsignedVarInt(0);
        w.WriteUnsignedVarInt(1);
        // interact pitch/yaw
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        // tick + delta
        w.WriteUnsignedVarLong(42);
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        // block actions: 1 × predict_destroy at 10, -60, 20 face 1
        w.WriteVarInt(1);
        w.WriteVarInt(PlayerAuthInputPacket.ActionPredictDestroy);
        w.WriteVarInt(10);
        w.WriteVarInt(-60);
        w.WriteVarInt(20);
        w.WriteVarInt(1);
        // trailing analogue/camera/raw
        for (var i = 0; i < 7; i++)
            w.WriteFloat(0, BinaryStream.Endianess.Little);

        var payload = w.GetBufferDisposing().ToArray();
        var stream = new BinaryStream(payload);
        var packet = new PlayerAuthInputPacket();
        packet.Decode(ref stream);

        Assert.Equal(1.5f, packet.Pitch);
        Assert.Single(packet.BlockActions);
        Assert.Equal(PlayerAuthInputPacket.ActionPredictDestroy, packet.BlockActions[0].Action);
        Assert.Equal(10, packet.BlockActions[0].BlockX);
        Assert.Equal(-60, packet.BlockActions[0].BlockY);
        Assert.Equal(20, packet.BlockActions[0].BlockZ);
    }
}

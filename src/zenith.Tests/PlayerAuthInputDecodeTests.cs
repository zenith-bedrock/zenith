using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Wire shapes here are byte-verified against a live capture of a real client
/// (bedrock-protocol) cross-checked with minecraft-data's 1.26.40 protocol.json — see ADR §89.
/// </summary>
public class PlayerAuthInputDecodeTests
{
    private static void WritePoseAndModes(ref BinaryStream w, float pitch = 1.5f)
    {
        w.WriteFloat(pitch, BinaryStream.Endianess.Little); // pitch
        w.WriteFloat(0, BinaryStream.Endianess.Little); // yaw
        w.WriteFloat(0, BinaryStream.Endianess.Little); // pos x
        w.WriteFloat(0, BinaryStream.Endianess.Little); // pos y
        w.WriteFloat(0, BinaryStream.Endianess.Little); // pos z
        w.WriteFloat(0, BinaryStream.Endianess.Little); // move x
        w.WriteFloat(0, BinaryStream.Endianess.Little); // move z
        w.WriteFloat(0, BinaryStream.Endianess.Little); // head_yaw
    }

    private static void WriteEmptyInputData(ref BinaryStream w) => w.WriteBool(false);

    private static void WriteInputDataFlags(ref BinaryStream w, params int[] flags)
    {
        w.WriteBool(true);
        w.WriteUnsignedVarInt(flags.Length);
        foreach (var f in flags)
            w.WriteVarInt(f);
    }

    private static void WriteModesAndTick(ref BinaryStream w, long tick = 1)
    {
        w.WriteUnsignedVarInt(1); // input_mode
        w.WriteUnsignedVarInt(0); // play_mode
        w.WriteVarInt(2); // interaction_model
        w.WriteFloat(0, BinaryStream.Endianess.Little); // interact pitch
        w.WriteFloat(0, BinaryStream.Endianess.Little); // interact yaw
        w.WriteUnsignedVarLong(tick);
        w.WriteFloat(0, BinaryStream.Endianess.Little); // pos_delta
        w.WriteFloat(0, BinaryStream.Endianess.Little);
        w.WriteFloat(0, BinaryStream.Endianess.Little);
    }

    private static void WriteNoTransactionNoItemStackRequest(ref BinaryStream w)
    {
        w.WriteBool(false); // transaction_presence (decorative)
        w.WriteBool(false); // transaction option
        w.WriteBool(false); // item_stack_request_presence (decorative)
        w.WriteBool(false); // item_stack_request option
    }

    private static void WriteNoVehicleTail(ref BinaryStream w)
    {
        w.WriteBool(false); // vehicle_rotation_presence (decorative)
        w.WriteBool(false); // vehicle_rotation option
        w.WriteBool(false); // predicted_vehicle_presence (decorative)
        w.WriteBool(false); // predicted_vehicle option
        for (var i = 0; i < 7; i++)
            w.WriteFloat(0, BinaryStream.Endianess.Little); // analogue/camera/raw
    }

    [Fact]
    public void Decode_no_flags_no_block_actions()
    {
        var w = new BinaryStream();
        WritePoseAndModes(ref w);
        WriteEmptyInputData(ref w);
        WriteModesAndTick(ref w);
        WriteNoTransactionNoItemStackRequest(ref w);
        w.WriteBool(false); // block_action_presence (decorative)
        w.WriteBool(false); // block_action option
        WriteNoVehicleTail(ref w);

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new PlayerAuthInputPacket();
        packet.Decode(ref stream);

        Assert.Equal(1.5f, packet.Pitch);
        Assert.Empty(packet.BlockActions);
        Assert.Null(packet.ItemInteraction);
        Assert.False(packet.InputSneaking);
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void Decode_input_data_list_sets_sneaking_and_sprint_flags()
    {
        var w = new BinaryStream();
        WritePoseAndModes(ref w);
        WriteInputDataFlags(
            ref w,
            PlayerAuthInputPacket.InputFlagSneaking,
            PlayerAuthInputPacket.InputFlagStartSprinting,
            PlayerAuthInputPacket.InputFlagMissedSwing);
        WriteModesAndTick(ref w);
        WriteNoTransactionNoItemStackRequest(ref w);
        w.WriteBool(false);
        w.WriteBool(false);
        WriteNoVehicleTail(ref w);

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new PlayerAuthInputPacket();
        packet.Decode(ref stream);

        Assert.True(packet.InputSneaking);
        Assert.True(packet.InputStartSprinting);
        Assert.True(packet.InputMissedSwing);
        Assert.False(packet.InputStopSprinting);
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void Decode_predict_destroy_block_action()
    {
        var w = new BinaryStream();
        WritePoseAndModes(ref w);
        WriteEmptyInputData(ref w);
        WriteModesAndTick(ref w, tick: 42);
        WriteNoTransactionNoItemStackRequest(ref w);

        // block_action option: present, 1 entry (predict_destroy at 10,-60,20 face 1)
        w.WriteBool(false); // block_action_presence (decorative)
        w.WriteBool(true); // block_action option
        w.WriteUnsignedVarInt(1);
        w.WriteVarInt(PlayerAuthInputPacket.ActionPredictDestroy);
        w.WriteVarInt(10);
        w.WriteVarInt(-60);
        w.WriteVarInt(20);
        w.WriteVarInt(1);

        WriteNoVehicleTail(ref w);

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new PlayerAuthInputPacket();
        packet.Decode(ref stream);

        Assert.True(packet.InputPerformBlockActions);
        Assert.Single(packet.BlockActions);
        Assert.Equal(PlayerAuthInputPacket.ActionPredictDestroy, packet.BlockActions[0].Action);
        Assert.Equal(10, packet.BlockActions[0].BlockX);
        Assert.Equal(-60, packet.BlockActions[0].BlockY);
        Assert.Equal(20, packet.BlockActions[0].BlockZ);
        Assert.Equal(1, packet.BlockActions[0].Face);
        Assert.True(stream.IsEndOfFile);
    }

    private static void WriteUseItemTransaction(
        ref BinaryStream w, int actionType, int x, int y, int z, int face, int hotbar)
    {
        w.WriteBool(false); // transaction_presence (decorative)
        w.WriteBool(true); // transaction option

        w.WriteVarInt(0); // legacy_request_id
        w.WriteBool(false); // legacy_transactions option

        w.WriteBool(false); // actions_presence (decorative)
        w.WriteBool(false); // actions option

        w.WriteVarInt(actionType);
        w.WriteByte(0); // trigger_type
        w.WriteVarInt(x);
        w.WriteVarInt(y);
        w.WriteVarInt(z);
        w.WriteByte((byte)face);
        w.WriteVarInt(hotbar);

        // held_item: ItemV4, air (network_id=0)
        w.WriteShort(0, BinaryStream.Endianess.Little);
        w.WriteUShort(0, BinaryStream.Endianess.Little);
        w.WriteUnsignedVarInt(0);
        w.WriteBool(false); // has_stack_id
        w.WriteUnsignedVarInt(0); // block_runtime_id
        w.WriteUnsignedVarInt(0); // extra length

        for (var i = 0; i < 6; i++)
            w.WriteFloat(0, BinaryStream.Endianess.Little); // player_pos + click_pos
        w.WriteUnsignedVarInt(0); // block_runtime_id
        w.WriteByte(0); // client_prediction
        w.WriteByte(0); // client_cooldown_state

        w.WriteBool(false); // item_stack_request_presence (decorative)
        w.WriteBool(false); // item_stack_request option
    }

    [Fact]
    public void Decode_use_item_transaction_destroy_block()
    {
        var w = new BinaryStream();
        WritePoseAndModes(ref w, pitch: 1f);
        WriteEmptyInputData(ref w);
        WriteModesAndTick(ref w);
        WriteUseItemTransaction(ref w, InventoryTransactionPacket.UseDestroyBlock, 3, 4, 5, 1, 0);

        w.WriteBool(false); // block_action_presence (decorative)
        w.WriteBool(true); // block_action option
        w.WriteUnsignedVarInt(1);
        w.WriteVarInt(PlayerAuthInputPacket.ActionPredictDestroy);
        w.WriteVarInt(3);
        w.WriteVarInt(4);
        w.WriteVarInt(5);
        w.WriteVarInt(1);

        WriteNoVehicleTail(ref w);

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new PlayerAuthInputPacket();
        packet.Decode(ref stream);

        Assert.Single(packet.BlockActions);
        Assert.Equal(PlayerAuthInputPacket.ActionPredictDestroy, packet.BlockActions[0].Action);
        Assert.Equal(3, packet.BlockActions[0].BlockX);
        Assert.Equal(4, packet.BlockActions[0].BlockY);
        Assert.Equal(5, packet.BlockActions[0].BlockZ);

        Assert.NotNull(packet.ItemInteraction);
        Assert.Equal(InventoryTransactionPacket.UseDestroyBlock, packet.ItemInteraction!.Value.ActionType);
        Assert.Equal(3, packet.ItemInteraction.Value.BlockX);
        Assert.Equal(4, packet.ItemInteraction.Value.BlockY);
        Assert.Equal(5, packet.ItemInteraction.Value.BlockZ);
        Assert.Equal(0, packet.ItemInteraction.Value.HotbarSlot);
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void Decode_use_item_transaction_click_block_for_chest()
    {
        var w = new BinaryStream();
        WritePoseAndModes(ref w, pitch: 1f);
        WriteEmptyInputData(ref w);
        WriteModesAndTick(ref w);
        WriteUseItemTransaction(ref w, InventoryTransactionPacket.UseClickBlock, 8, -60, 12, 1, 0);

        w.WriteBool(false); // block_action_presence (decorative)
        w.WriteBool(false); // block_action option

        WriteNoVehicleTail(ref w);

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new PlayerAuthInputPacket();
        packet.Decode(ref stream);

        Assert.NotNull(packet.ItemInteraction);
        Assert.Equal(InventoryTransactionPacket.UseClickBlock, packet.ItemInteraction!.Value.ActionType);
        Assert.Equal(8, packet.ItemInteraction.Value.BlockX);
        Assert.Equal(-60, packet.ItemInteraction.Value.BlockY);
        Assert.Equal(12, packet.ItemInteraction.Value.BlockZ);
        Assert.Empty(packet.BlockActions);
        Assert.True(stream.IsEndOfFile);
    }
}

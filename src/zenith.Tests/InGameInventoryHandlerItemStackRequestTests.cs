using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;
using Zenith.Session.Handler;

namespace Zenith.Tests;

/// <summary>
/// <see cref="Zenith.Session.Handler.InGameSessionHandler"/>'s <c>HandleItemStackRequest</c> →
/// <c>TryMapAction</c> → <c>TryGetSessionRequirement</c> pipeline had no test driving the real wire
/// decode path — existing inventory tests exercise <c>InventoryStackAction</c>/systems directly, and
/// <c>GamePacketDispatchTests</c> substitutes a recording handler that bypasses this logic entirely.
/// This drives one real, minimal case (MineBlock — the one action that needs no open-container
/// session) end to end: wire bytes in, a queued domain intent out.
/// </summary>
public class InGameInventoryHandlerItemStackRequestTests
{
    /// <summary>
    /// Wire shape per ItemStackRequestPacket.Decode: MineBlock's Cereal union variant is 9 (its
    /// legacy action id 11, minus 2, since it sorts after the two omitted container action ids —
    /// see ItemStackRequestPacket.LegacyActionId).
    /// </summary>
    private static byte[] BuildMineBlockBatch(int hotbarSlot, int stackNetworkId, int requestId = 7)
    {
        var header = new BinaryStream();
        header.WriteUnsignedVarInt((int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET);

        var body = new BinaryStream();
        body.WriteUnsignedVarInt(1); // request count
        body.WriteVarInt(requestId);
        body.WriteUnsignedVarInt(1); // action count
        body.WriteUnsignedVarInt(9); // Cereal variant for MineBlock (legacy id 11)
        body.WriteByte(ItemStackRequestPacket.ActionMineBlock);
        body.WriteVarInt(hotbarSlot);
        body.WriteVarInt(0); // predicted durability — ignored, no authoritative durability state yet
        body.WriteInt(stackNetworkId, BinaryStream.Endianess.Little);
        body.WriteUnsignedVarInt(0); // filter strings
        body.WriteInt(0, BinaryStream.Endianess.Little); // cause
        var bodyBytes = body.TakeOwnedBuffer();

        header.Write(bodyBytes);
        var entry = header.TakeOwnedBuffer();

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(entry.Length);
        writer.Write(entry);
        return writer.TakeOwnedBuffer();
    }

    [Fact]
    public void MineBlock_action_for_a_valid_hotbar_slot_queues_a_mine_intent()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("miner");
        player.Session.SetHandler(new InGameSessionHandler());

        var batch = BuildMineBlockBatch(hotbarSlot: 0, stackNetworkId: 55);
        var stream = new BinaryStream(batch);
        player.Session.HandleGamePacket(ref stream);

        Assert.True(player.TryConsumeInventoryStack(out var intent));
        Assert.Equal(7, intent.RequestId);
        var action = Assert.Single(intent.Actions);
        Assert.Equal(Zenith.Player.InventoryStackActionKind.Mine, action.Kind);
    }

    /// <summary>An out-of-range hotbar slot must be rejected (mapping fails), not silently mapped
    /// or crash the handler — the same class of validation gap the audit flagged as untested.</summary>
    [Fact]
    public void MineBlock_action_for_an_invalid_hotbar_slot_is_rejected_not_queued()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bad-slot-miner");
        player.Session.SetHandler(new InGameSessionHandler());

        var batch = BuildMineBlockBatch(hotbarSlot: 999, stackNetworkId: 55);
        var stream = new BinaryStream(batch);
        player.Session.HandleGamePacket(ref stream);

        Assert.False(player.TryConsumeInventoryStack(out _));
    }
}

using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Inventory;
using System.Collections.Generic;
using Zenith.Binary;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>Partial: see <see cref="InGameSessionHandler"/>.</summary>
partial class InGameSessionHandler
{
    private static void HandleInteract(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<InteractPacket>(session, ref stream, "Interact", out var packet))
            return;
        var player = session.Player;
        if (player is null) return;
        if (packet.Action != InteractPacket.ActionOpenInventory) return;

        // OpenInventory is a self-interaction. Do not turn an arbitrary actor id into a
        // player-container transition; the target identity is part of the Bedrock contract.
        if (packet.TargetActorRuntimeId != player.RuntimeId)
        {
            session.Context.Logger.Debug(
                $"Rejected Interact(OpenInventory): target {packet.TargetActorRuntimeId} is not {player.RuntimeId}.");
            return;
        }

        if (!player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenInventory()))
            session.Context.Logger.Debug($"Dropped inventory open from {player.Username}: window queue full.");
    }

    private static void HandleContainerClose(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<ContainerClosePacket>(session, ref stream, "ContainerClose", out var packet))
            return;
        var player = session.Player;
        if (player is null) return;

        if (!player.Windows.SubmitWindowIntent(InventoryWindowIntent.Close(packet.WindowId, packet.WindowType)))
            session.Context.Logger.Debug($"Dropped container close from {player.Username}: window queue full.");
    }

    private static void HandleMobEquipment(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<MobEquipmentPacket>(session, ref stream, "MobEquipment", out var packet))
            return;
        var player = session.Player;
        if (player is null) return;

        // MobEquipment is only valid for the sender's own actor. It is not a generic
        // peer-equipment update channel in the serverbound direction.
        if (packet.ActorRuntimeId != player.RuntimeId)
        {
            session.Context.Logger.Debug($"Rejected MobEquipment: actor {packet.ActorRuntimeId} is not {player.RuntimeId}.");
            return;
        }

        // Bedrock sends this while moving an item to offhand too. The offhand is owned and moved by
        // the ISR path (container 34), so this is only an echo of that move: it must not change the
        // main hand and is not a second way to write the offhand.
        if (packet.WindowId == MobEquipmentPacket.WindowOffhand)
            return;

        if (packet.WindowId != MobEquipmentPacket.WindowInventory ||
            packet.InventorySlot != packet.HotbarSlot ||
            !PlayerInventory.IsValidHotbarSlot(packet.HotbarSlot))
        {
            session.Context.Logger.Debug(
                $"Rejected MobEquipment: window={packet.WindowId}, inventory={packet.InventorySlot}, hotbar={packet.HotbarSlot}.");
            session.Protocol.Inventory.ResyncActiveInventoryView(player);
            return;
        }

        var claimed = new DecodedTransactionItem
        {
            NetworkId = packet.Item.NetworkId,
            Count = packet.Item.Count,
            Meta = packet.Item.Meta,
            BlockRuntimeId = packet.Item.BlockRuntimeId,
            StackNetworkId = packet.Item.StackNetworkId
        };
        if (!MatchesCurrentStack(session, player, packet.HotbarSlot, claimed) ||
            !session.Protocol.Inventory.MatchesAdvertisedStackNetId(
                InventorySlotReference.Player(packet.HotbarSlot), claimed.StackNetworkId))
        {
            session.Context.Logger.Debug($"Rejected MobEquipment: held stack mismatch in hotbar {packet.HotbarSlot}.");
            session.Protocol.Inventory.ResyncActiveInventoryView(player);
            return;
        }

        // Selection is real-time control state, but only after the wire claim is coherent with
        // the authoritative inventory snapshot (same policy as UseItem/UseOnActor).
        player.SelectedHotbarSlot = packet.HotbarSlot;
    }
}

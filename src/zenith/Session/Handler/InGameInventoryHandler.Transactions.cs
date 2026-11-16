using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Inventory;
using System.Collections.Generic;
using Zenith.Binary;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>Legacy InventoryTransaction: held-stack checks and baking the old drop path into the same actions.</summary>
partial class InGameSessionHandler
{
    private static void HandleInventoryTransaction(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<InventoryTransactionPacket>(session, ref stream, "InventoryTransaction", out var packet))
            return;
        var player = session.Player;
        if (player is null || player.IsDead) return;

        switch (packet.TransactionType)
        {
            case InventoryTransactionPacket.TypeNormal:
                HandleNormalInventoryTransaction(session, player, packet);
                break;

            case InventoryTransactionPacket.TypeMismatch:
                // The client explicitly reports a predicted transaction mismatch. Do not infer
                // a mutation from it: push the current authoritative view back instead.
                if (IsCorrelatableLegacyRequest(packet.LegacyRequestId))
                    session.Protocol.Inventory.SendItemStackResponseError(packet.LegacyRequestId);
                session.Protocol.Inventory.ResyncActiveInventoryView(player);
                break;

            case InventoryTransactionPacket.TypeItemUse:
                if (packet.Hand != InventoryTransactionPacket.HandMainhand)
                {
                    session.Context.Logger.Debug($"Rejected UseItem from unsupported hand {packet.Hand} for {player.Username}.");
                    break;
                }

                HandleUseItem(
                    session,
                    player,
                    packet.UseActionType,
                    packet.BlockX,
                    packet.BlockY,
                    packet.BlockZ,
                    packet.BlockFace,
                    packet.HotbarSlot,
                    packet.ClickedBlockRuntimeId,
                    packet.ClickY,
                    packet.HeldItem);
                break;
            case InventoryTransactionPacket.TypeItemUseOnActor:
                if (packet.ActorActionType is not (InventoryTransactionPacket.ActorAttack or InventoryTransactionPacket.ActorInteract))
                {
                    session.Context.Logger.Debug(
                        $"Rejected ItemUseOnActor: unknown action {packet.ActorActionType} from {player.Username}.");
                    break;
                }

                if (!TryApplyTransactionHeldStack(session, player, packet.HotbarSlot, packet.HeldItem, "ItemUseOnActor"))
                    break;

                if (packet.ActorActionType == InventoryTransactionPacket.ActorAttack)
                {
                    player.Intents.SubmitAttackIntent(packet.TargetActorRuntimeId);
                    session.Context.Logger.Debug(
                        $"Queued actor attack from {player.Username} -> {packet.TargetActorRuntimeId} (slot {packet.HotbarSlot})");
                    PlayerVisibility.RelaySwingArm(
                        player,
                        session.Context.PlayerManager.SnapshotOnline(),
                        swingSource: "attack");
                }
                else if (packet.ActorActionType == InventoryTransactionPacket.ActorInteract)
                {
                    player.Intents.SubmitInteractIntent(packet.TargetActorRuntimeId);
                }
                break;

            case InventoryTransactionPacket.TypeItemRelease:
                if (packet.ReleaseActionType != InventoryTransactionPacket.ReleaseActionRelease)
                {
                    session.Context.Logger.Debug(
                        $"Rejected ItemRelease: unknown action {packet.ReleaseActionType} from {player.Username}.");
                    break;
                }

                // Release cancels food use; it never starts another use or projectile.
                _ = TryApplyTransactionHeldStack(session, player, packet.HotbarSlot, packet.HeldItem, "ItemRelease");
                _ = player.Intents.SubmitCancelFoodUse();
                break;
        }
    }

    private static bool TryApplyTransactionHeldStack(
        NetworkSession session,
        Player.Player player,
        int hotbarSlot,
        in DecodedTransactionItem heldItem,
        string transactionKind)
    {
        if (!PlayerInventory.IsValidHotbarSlot(hotbarSlot))
        {
            session.Context.Logger.Debug($"Rejected {transactionKind}: hotbar {hotbarSlot}");
            return false;
        }

        // The hotbar index and ItemV4 descriptor are client claims. Resolve both against the
        // last authoritative view before creating an attack, block edit, eating or release intent.
        // A mismatch is a client/server desync, not a reason to guess a mutation.
        var expected = session.Protocol.Inventory.DescribeSlot(player.Inventory, hotbarSlot);
        var stackMatches = MatchesWireStack(heldItem, expected);
        var advertisedIdMatches = session.Protocol.Inventory.MatchesAdvertisedStackNetId(
            InventorySlotReference.Player(hotbarSlot), heldItem.StackNetworkId);
        if (!stackMatches || !advertisedIdMatches)
        {
            session.Context.Logger.Debug(
                $"Rejected {transactionKind}: held stack mismatch in hotbar {hotbarSlot}; " +
                $"claimed item/count/meta/block={heldItem.NetworkId}/{heldItem.Count}/{heldItem.Meta}/{heldItem.BlockRuntimeId}, " +
                $"expected={expected.NetworkId}/{expected.Count}/{expected.Meta}/{expected.BlockRuntimeId}, " +
                $"stackNetId={heldItem.StackNetworkId} advertisedIdMatches={advertisedIdMatches}.");
            session.Protocol.Inventory.ResyncActiveInventoryView(player);
            return false;
        }

        // Hotbar selection is a validated real-time input boundary (ADR §80). It does not
        // mutate inventory contents; later gameplay resolves the authoritative slot itself.
        player.SelectedHotbarSlot = hotbarSlot;
        return true;
    }

    /// <summary>
    /// Bedrock still uses a Normal inventory transaction for the hotkey/drop-outside-menu path.
    /// Only accept its canonical two-action conservation shape; other Normal transactions (book
    /// edits and legacy UI traffic) are resynchronised rather than guessed at.
    /// </summary>
    private static void HandleNormalInventoryTransaction(
        NetworkSession session,
        Player.Player player,
        InventoryTransactionPacket packet)
    {
        // Emptying the source may carry no legacy ID. Snapshot preconditions protect that
        // operation on tick; nonempty remainders still use the correlatable legacy form.
        if ((packet.LegacyRequestId != 0 &&
                (!packet.HasLegacySetItemSlots || !IsCorrelatableLegacyRequest(packet.LegacyRequestId))) ||
            !TryBakeLegacyDrop(session, player, packet, out var intent))
        {
            if (IsCorrelatableLegacyRequest(packet.LegacyRequestId))
                session.Protocol.Inventory.SendItemStackResponseError(packet.LegacyRequestId);
            session.Protocol.Inventory.ResyncActiveInventoryView(player);
            session.Context.Logger.Debug($"Rejected Normal inventory transaction from {player.Username}.");
            return;
        }

        if (!player.Intents.SubmitInventoryStack(intent))
        {
            session.Protocol.Inventory.SendItemStackResponseError(packet.LegacyRequestId);
            session.Protocol.Inventory.ResyncActiveInventoryView(player);
            session.Context.Logger.Debug($"Dropped Normal inventory transaction from {player.Username}: queue full.");
        }
    }

    /// <summary>
    /// Protocol 2168 uses even negative legacy IDs as correlatable ItemStackResponse request
    /// identifiers. Keep this predicate shared by accepted and rejected branches so a client
    /// never waits indefinitely after a conservatively rejected legacy transaction.
    /// </summary>
    private static bool IsCorrelatableLegacyRequest(int requestId) =>
        requestId < -1 && (requestId & 1) == 0;

    private static bool TryBakeLegacyDrop(
        NetworkSession session,
        Player.Player player,
        InventoryTransactionPacket packet,
        out InventoryStackIntent intent)
    {
        intent = default;
        if (packet.Actions.Length != 2) return false;

        DecodedInventoryTransactionAction? source = null;
        DecodedInventoryTransactionAction? world = null;
        foreach (var action in packet.Actions)
        {
            if (action.SourceType == InventoryTransactionPacket.SourceContainer &&
                action.WindowId == InventoryContentPacket.WindowInventory)
            {
                if (source.HasValue || action.Slot is < 0 or >= PlayerInventory.FullInventorySize)
                    return false;
                source = action;
            }
            else if (action.SourceType == InventoryTransactionPacket.SourceWorld && action.Slot == 0)
            {
                if (world.HasValue || !action.OldItem.IsEmpty)
                    return false;
                world = action;
            }
            else
            {
                return false;
            }
        }

        if (source is not { } slotAction || world is not { } worldAction || worldAction.NewItem.IsEmpty)
            return false;

        var slot = slotAction.Slot;
        var actual = player.Inventory.Get(slot);
        // The old descriptor names the authoritative precondition. Its positive stack-network
        // id must still be the one last advertised for this slot; otherwise a delayed legacy
        // drop could remove a newer stack with the same item/count identity.
        if (actual.IsEmpty ||
            !MatchesCurrentStack(session, player, slot, slotAction.OldItem) ||
            !session.Protocol.Inventory.MatchesAdvertisedStackNetId(
                InventorySlotReference.Player(slot), slotAction.OldItem.StackNetworkId))
            return false;

        var dropCount = worldAction.NewItem.Count;
        if (dropCount is 0 || dropCount > actual.Count ||
            !MatchesStackIdentity(session, actual, worldAction.NewItem))
            return false;

        var expectedRemainder = dropCount == actual.Count
            ? NetworkItemStack.Empty
            : session.Protocol.Inventory.DescribeSlot(
                player.Inventory,
                slot) with { Count = checked((ushort)(actual.Count - dropCount)) };
        if (!MatchesWireStack(slotAction.NewItem, expectedRemainder))
            return false;
        if (packet.LegacyRequestId == 0 && dropCount != actual.Count) return false;

        intent = InventoryStackIntent.Create(
            packet.LegacyRequestId,
            [InventoryStackAction.Drop(InventorySlotReference.Player(slot), dropCount,
                new WireSlot(InventoryContainerMap.Inventory, (byte)slot, slotAction.OldItem.StackNetworkId)) with
                { ExpectedSourceStack = actual }]) with { UncorrelatedLegacyDrop = packet.LegacyRequestId == 0 };
        return true;
    }

    private static bool MatchesCurrentStack(
        NetworkSession session,
        Player.Player player,
        int slot,
        in DecodedTransactionItem candidate) =>
        MatchesWireStack(candidate, session.Protocol.Inventory.DescribeSlot(player.Inventory, slot));

    private static bool MatchesStackIdentity(
        NetworkSession session,
        InventorySlot actual,
        in DecodedTransactionItem candidate)
    {
        var expected = session.Protocol.Inventory.DescribeStack(actual.Id, actual.Count);
        return candidate.NetworkId == expected.NetworkId &&
               candidate.Meta == expected.Meta &&
               candidate.BlockRuntimeId == expected.BlockRuntimeId;
    }

    private static bool MatchesWireStack(in DecodedTransactionItem candidate, in NetworkItemStack expected) =>
        candidate.NetworkId == expected.NetworkId &&
        candidate.Count == expected.Count &&
        candidate.Meta == expected.Meta &&
        candidate.BlockRuntimeId == expected.BlockRuntimeId;
}

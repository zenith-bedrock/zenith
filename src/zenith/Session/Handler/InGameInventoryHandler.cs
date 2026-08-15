using System.Collections.Generic;
using Zenith.Raknet.Stream;
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

        if (!player.SubmitWindowIntent(InventoryWindowIntent.OpenInventory()))
            session.Context.Logger.Debug($"Dropped inventory open from {player.Username}: window queue full.");
    }

    private static void HandleContainerClose(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<ContainerClosePacket>(session, ref stream, "ContainerClose", out var packet))
            return;
        var player = session.Player;
        if (player is null) return;

        if (!player.SubmitWindowIntent(InventoryWindowIntent.Close(packet.WindowId, packet.WindowType)))
            session.Context.Logger.Debug($"Dropped container close from {player.Username}: window queue full.");
    }

    private static void HandleItemStackRequest(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<ItemStackRequestPacket>(session, ref stream, "ItemStackRequest", out var packet))
            return;
        var player = session.Player;
        if (player is null) return;
        HandleItemStackRequests(session, player, packet.Requests);
    }

    /// <summary>Processes either the standalone packet array or one request embedded in AuthInput.</summary>
    private static void HandleItemStackRequests(
        NetworkSession session,
        Player.Player player,
        ReadOnlySpan<DecodedItemStackRequest> requests)
    {
        if (player.IsDead)
        {
            foreach (var request in requests)
                RejectIsr(session, player, request.RequestId);
            return;
        }

        foreach (var request in requests)
        {
            if (!request.AllSupported || request.Actions.Length == 0)
            {
                RejectIsr(session, player, request.RequestId);
                continue;
            }

            var baked = new List<InventoryStackAction>(request.Actions.Length);
            var mapOk = true;

            foreach (var action in request.Actions)
            {
                if (!TryMapAction(action, out var mapped))
                {
                    mapOk = false;
                    break;
                }

                baked.Add(mapped);
            }

            if (!mapOk || baked.Count == 0)
            {
                RejectIsr(session, player, request.RequestId);
                continue;
            }

            var hasCreative = false;
            var hasRecipe = false;
            foreach (var a in baked)
            {
                if (a.Kind == InventoryStackActionKind.CraftCreative) hasCreative = true;
                else if (a.Kind == InventoryStackActionKind.CraftRecipe) hasRecipe = true;
            }

            if (hasCreative)
            {
                if (hasRecipe || player.GameMode != GameMode.Creative)
                {
                    RejectIsr(session, player, request.RequestId);
                    continue;
                }

                var catalogOk = true;
                foreach (var a in baked)
                {
                    if (a.Kind != InventoryStackActionKind.CraftCreative) continue;
                    if (!session.Context.Creative.TryGet(a.CreativeNetId, out _, out _))
                    {
                        catalogOk = false;
                        break;
                    }
                }

                if (!catalogOk)
                {
                    RejectIsr(session, player, request.RequestId);
                    continue;
                }
            }

            uint expectedOpenContainerGeneration = 0;
            if (!TryGetSessionRequirement(baked, out var requiresSession, out var requiredTarget))
            {
                RejectIsr(session, player, request.RequestId);
                continue;
            }

            if (requiresSession)
            {
                // Bind this wire action to the session visible at decode time. The tick owns the
                // state and rejects the intent if close/reopen changed that generation meanwhile.
                // A just-received Interact(OpenInventory) is still only a mailbox entry here;
                // retain the request when its latest pending window will establish the required
                // target, then let InventorySystem open it before resolving this request.
                if (player.TryGetOpenContainerSession(out var openContainer) &&
                    (requiredTarget is null || openContainer.Target == requiredTarget))
                {
                    expectedOpenContainerGeneration = openContainer.Generation;
                }
                else if (!player.HasPendingContainerOpen(requiredTarget))
                {
                    RejectIsr(session, player, request.RequestId);
                    continue;
                }
            }

            var intent = InventoryStackIntent.Create(
                request.RequestId,
                baked.ToArray(),
                expectedOpenContainerGeneration,
                requiresSession,
                requiredTarget);
            if (!player.SubmitInventoryStack(intent))
            {
                session.Context.Logger.Debug($"Dropped ISR from {player.Username}: inventory-stack queue full.");
                RejectIsr(session, player, request.RequestId);
            }
        }
    }

    /// <summary>
    /// Maps one wire ISR action to its domain <see cref="InventoryStackAction"/> (Phase XIII.3 —
    /// extracted from <see cref="HandleItemStackRequest"/>, which had grown to ~160 lines mixing
    /// this per-action mapping with request-level validation). Returns false only for the two slot
    /// container-id lookups that can fail (Drop's source, Transfer/Swap's source+destination); every
    /// other action type always maps.
    /// </summary>
    private static bool TryMapAction(in DecodedStackRequestAction action, out InventoryStackAction baked)
    {
        if (action.ActionType == ItemStackRequestPacket.ActionCraftCreative)
        {
            // NumberOfCrafts / CraftTimes is protocol boilerplate — ignored.
            baked = InventoryStackAction.CraftCreative(action.CreativeNetId);
            return true;
        }

        if (action.ActionType == ItemStackRequestPacket.ActionCraftRecipe)
        {
            if (action.CraftTimes == 0)
            {
                baked = default;
                return false;
            }

            baked = InventoryStackAction.Craft(action.RecipeNetId, action.CraftTimes);
            return true;
        }

        if (action.ActionType == ItemStackRequestPacket.ActionCreate)
        {
            if (action.ResultSlot != 0)
            {
                baked = default;
                return false;
            }

            baked = InventoryStackAction.CreateOutput();
            return true;
        }

        if (action.ActionType == ItemStackRequestPacket.ActionConsume ||
            action.ActionType == ItemStackRequestPacket.ActionCraftResultsDeprecated)
        {
            baked = InventoryStackAction.ConsumeNoOp();
            return true;
        }

        if (action.ActionType == ItemStackRequestPacket.ActionDrop)
        {
            if (!TryMapStaticStackRequestSlot(action.Source, out var from))
            {
                baked = default;
                return false;
            }

            var fromWire = new WireSlot(
                action.Source.Container.ContainerId,
                action.Source.Slot,
                action.Source.StackNetworkId);
            baked = InventoryStackAction.Drop(from, action.Count, fromWire);
            return true;
        }

        if (action.ActionType == ItemStackRequestPacket.ActionMineBlock)
        {
            if (!PlayerInventory.IsValidHotbarSlot(action.HotbarSlot))
            {
                baked = default;
                return false;
            }

            // MineBlock does not carry a FullContainerName. Bedrock defines its hotbar index
            // against the ordinary inventory container (not the transient UI window).
            baked = InventoryStackAction.Mine(
                InventorySlotReference.Player(action.HotbarSlot),
                new WireSlot(InventoryContainerMap.Inventory, (byte)action.HotbarSlot, action.StackNetworkId));
            return true;
        }

        if (action.ActionType == ItemStackRequestPacket.ActionDestroy)
        {
            if (!TryMapStaticStackRequestSlot(action.Source, out var from))
            {
                baked = default;
                return false;
            }

            baked = InventoryStackAction.Destroy(
                from,
                action.Count,
                new WireSlot(action.Source.Container.ContainerId, action.Source.Slot, action.Source.StackNetworkId));
            return true;
        }

        if (!TryMapStaticStackRequestSlot(action.Source, out var sourceReference) ||
            !TryMapStaticStackRequestSlot(action.Destination, out var destinationReference))
        {
            baked = default;
            return false;
        }

        var srcWire = new WireSlot(
            action.Source.Container.ContainerId,
            action.Source.Slot,
            action.Source.StackNetworkId);
        var dstWire = new WireSlot(
            action.Destination.Container.ContainerId,
            action.Destination.Slot,
            action.Destination.StackNetworkId);

        baked = action.ActionType == ItemStackRequestPacket.ActionSwap
            ? InventoryStackAction.Swap(sourceReference, destinationReference, srcWire, dstWire)
            : InventoryStackAction.Transfer(sourceReference, destinationReference, action.Count, srcWire, dstWire);
        return true;
    }

    /// <summary>
    /// Zenith has no dynamic-container domain yet. Do not silently erase a dynamic id and treat
    /// its slot as one of our static inventory/chest containers.
    /// </summary>
    private static bool TryMapStaticStackRequestSlot(
        in StackRequestSlotInfo wireSlot,
        out InventorySlotReference reference)
    {
        if (wireSlot.Container.DynamicId is not null)
        {
            reference = default;
            return false;
        }

        return InventoryContainerMap.TryMap(wireSlot.Container.ContainerId, wireSlot.Slot, out reference);
    }

    /// <summary>
    /// Determines whether an ISR request relies on a currently visible container session. Normal
    /// player-slot moves intentionally remain valid without one; crafting/output/cursor actions are
    /// UI state, while an open-container slot is specifically a chest view. A request cannot span a
    /// chest view and the player crafting UI because Bedrock never exposes both as one active view.
    /// </summary>
    private static bool TryGetSessionRequirement(
        IReadOnlyList<InventoryStackAction> actions,
        out bool requiresSession,
        out OpenContainerSession.TargetKind? requiredTarget)
    {
        requiresSession = false;
        requiredTarget = null;

        foreach (var action in actions)
        {
            if (action.Kind is InventoryStackActionKind.CraftRecipe or
                InventoryStackActionKind.CraftCreative or
                InventoryStackActionKind.Create or
                InventoryStackActionKind.Destroy)
            {
                if (!TryRequireTarget(OpenContainerSession.TargetKind.PlayerInventory, ref requiresSession, ref requiredTarget))
                    return false;
            }

            if (!TryApplyReferenceRequirement(action.From, ref requiresSession, ref requiredTarget) ||
                !TryApplyReferenceRequirement(action.To, ref requiresSession, ref requiredTarget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryApplyReferenceRequirement(
        in InventorySlotReference reference,
        ref bool requiresSession,
        ref OpenContainerSession.TargetKind? requiredTarget)
    {
        return reference.Area switch
        {
            InventorySlotArea.OpenContainer =>
                TryRequireTarget(OpenContainerSession.TargetKind.Chest, ref requiresSession, ref requiredTarget),
            InventorySlotArea.CraftGrid or InventorySlotArea.CraftResult =>
                TryRequireTarget(OpenContainerSession.TargetKind.PlayerInventory, ref requiresSession, ref requiredTarget),
            InventorySlotArea.Cursor => TryRequireAnySession(ref requiresSession),
            _ => true
        };
    }

    private static bool TryRequireTarget(
        OpenContainerSession.TargetKind target,
        ref bool requiresSession,
        ref OpenContainerSession.TargetKind? requiredTarget)
    {
        requiresSession = true;
        if (requiredTarget is { } existing && existing != target)
            return false;
        requiredTarget = target;
        return true;
    }

    private static bool TryRequireAnySession(ref bool requiresSession)
    {
        requiresSession = true;
        return true;
    }

    private static void RejectIsr(NetworkSession session, Player.Player player, int requestId)
    {
        session.Protocol.Inventory.SendItemStackResponseError(requestId);
        session.Protocol.Inventory.ResyncActiveInventoryView(player);
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

        // Bedrock sends this while moving an item to offhand too. Zenith does not own an
        // offhand domain slice yet, so acknowledge the packet without changing main hand.
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
                    player.SubmitAttackIntent(packet.TargetActorRuntimeId);
                    session.Context.Logger.Debug(
                        $"Queued actor attack from {player.Username} -> {packet.TargetActorRuntimeId} (slot {packet.HotbarSlot})");
                    PlayerVisibility.RelaySwingArm(
                        player,
                        session.Context.PlayerManager.SnapshotOnline(),
                        swingSource: "attack");
                }
                else if (packet.ActorActionType == InventoryTransactionPacket.ActorInteract)
                {
                    player.SubmitInteractIntent(packet.TargetActorRuntimeId);
                }
                break;

            case InventoryTransactionPacket.TypeItemRelease:
                if (packet.ReleaseActionType != InventoryTransactionPacket.ReleaseActionRelease)
                {
                    session.Context.Logger.Debug(
                        $"Rejected ItemRelease: unknown action {packet.ReleaseActionType} from {player.Username}.");
                    break;
                }

                // The current food/projectile slices commit from their initiating item-use
                // intent. Release only refreshes the selected input slot; treating it as a
                // second use would double-consume food or spawn a duplicate projectile.
                _ = TryApplyTransactionHeldStack(session, player, packet.HotbarSlot, packet.HeldItem, "ItemRelease");
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
        if (!MatchesCurrentStack(session, player, hotbarSlot, heldItem) ||
            !session.Protocol.Inventory.MatchesAdvertisedStackNetId(
                InventorySlotReference.Player(hotbarSlot), heldItem.StackNetworkId))
        {
            session.Context.Logger.Debug($"Rejected {transactionKind}: held stack mismatch in hotbar {hotbarSlot}.");
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
        // A correlatable legacy ID is required to make the tick-side replay ledger meaningful.
        // Without it, duplicated UDP input could apply two otherwise-valid drops before either
        // one changes the authoritative slot.
        if (!packet.HasLegacySetItemSlots ||
            !IsCorrelatableLegacyRequest(packet.LegacyRequestId) ||
            !TryBakeLegacyDrop(session, player, packet, out var intent))
        {
            if (IsCorrelatableLegacyRequest(packet.LegacyRequestId))
                session.Protocol.Inventory.SendItemStackResponseError(packet.LegacyRequestId);
            session.Protocol.Inventory.ResyncActiveInventoryView(player);
            session.Context.Logger.Debug($"Rejected Normal inventory transaction from {player.Username}.");
            return;
        }

        if (!player.SubmitInventoryStack(intent))
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

        intent = InventoryStackIntent.Create(
            packet.LegacyRequestId,
            [InventoryStackAction.Drop(InventorySlotReference.Player(slot), dropCount, default)]);
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

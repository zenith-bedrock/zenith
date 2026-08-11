using System.Collections.Generic;
using Zenith.Gameplay;
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
        var packet = DataPacket.From<InteractPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;
        if (packet.Action != InteractPacket.ActionOpenInventory) return;

        if (!player.SubmitWindowIntent(InventoryWindowIntent.OpenInventory()))
            session.Context.Logger.Debug($"Dropped inventory open from {player.Username}: window queue full.");
    }

    private static void HandleContainerClose(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<ContainerClosePacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        if (!player.SubmitWindowIntent(InventoryWindowIntent.Close(packet.WindowId, packet.WindowType)))
            session.Context.Logger.Debug($"Dropped container close from {player.Username}: window queue full.");
    }

    private static void HandleItemStackRequest(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<ItemStackRequestPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;
        if (player.IsDead)
        {
            foreach (var request in packet.Requests)
                RejectIsr(session, player, request.RequestId);
            return;
        }

        foreach (var request in packet.Requests)
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
                if (action.ActionType == ItemStackRequestPacket.ActionCraftCreative)
                {
                    // NumberOfCrafts / CraftTimes is protocol boilerplate — ignored.
                    baked.Add(InventoryStackAction.CraftCreative(action.CreativeNetId));
                    continue;
                }

                if (action.ActionType == ItemStackRequestPacket.ActionCraftRecipe)
                {
                    baked.Add(InventoryStackAction.Craft(action.RecipeNetId, action.CraftTimes));
                    continue;
                }

                if (action.ActionType == ItemStackRequestPacket.ActionCreate)
                {
                    baked.Add(InventoryStackAction.CreateOutput());
                    continue;
                }

                if (action.ActionType == ItemStackRequestPacket.ActionConsume ||
                    action.ActionType == ItemStackRequestPacket.ActionCraftResultsDeprecated)
                {
                    baked.Add(InventoryStackAction.ConsumeNoOp());
                    continue;
                }

                if (action.ActionType == ItemStackRequestPacket.ActionDrop)
                {
                    if (!InventoryContainerMap.TryMap(action.Source.Container.ContainerId, action.Source.Slot, out var from))
                    {
                        mapOk = false;
                        break;
                    }

                    var fromWire = new WireSlot(
                        action.Source.Container.ContainerId,
                        action.Source.Slot,
                        action.Source.StackNetworkId);
                    baked.Add(InventoryStackAction.Drop(from, action.Count, fromWire));
                    continue;
                }

                if (!InventoryContainerMap.TryMap(action.Source.Container.ContainerId, action.Source.Slot, out var sourceReference) ||
                    !InventoryContainerMap.TryMap(action.Destination.Container.ContainerId, action.Destination.Slot, out var destinationReference))
                {
                    mapOk = false;
                    break;
                }

                var srcWire = new WireSlot(
                    action.Source.Container.ContainerId,
                    action.Source.Slot,
                    action.Source.StackNetworkId);
                var dstWire = new WireSlot(
                    action.Destination.Container.ContainerId,
                    action.Destination.Slot,
                    action.Destination.StackNetworkId);

                if (action.ActionType == ItemStackRequestPacket.ActionSwap)
                    baked.Add(InventoryStackAction.Swap(sourceReference, destinationReference, srcWire, dstWire));
                else
                    baked.Add(InventoryStackAction.Transfer(sourceReference, destinationReference, action.Count, srcWire, dstWire));
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
                if (!player.TryGetOpenContainerSession(out var openContainer) ||
                    (requiredTarget is { } target && openContainer.Target != target))
                {
                    RejectIsr(session, player, request.RequestId);
                    continue;
                }

                expectedOpenContainerGeneration = openContainer.Generation;
            }

            var intent = InventoryStackIntent.Create(
                request.RequestId,
                baked.ToArray(),
                expectedOpenContainerGeneration);
            if (!player.SubmitInventoryStack(intent))
            {
                session.Context.Logger.Debug($"Dropped ISR from {player.Username}: inventory-stack queue full.");
                RejectIsr(session, player, request.RequestId);
            }
        }
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
                InventoryStackActionKind.Create)
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
        var packet = DataPacket.From<MobEquipmentPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        if (!PlayerInventory.IsValidHotbarSlot(packet.HotbarSlot))
        {
            session.Context.Logger.Debug($"Rejected MobEquipment: hotbar {packet.HotbarSlot}");
            return;
        }

        player.SelectedHotbarSlot = packet.HotbarSlot;
    }

    private static void HandleInventoryTransaction(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<InventoryTransactionPacket>(ref stream);
        var player = session.Player;
        if (player is null || player.IsDead) return;

        // TypeNormal does not populate UseActionType/HotbarSlot yet — do not mutate hotbar (ADR §17).
        switch (packet.TransactionType)
        {
            case InventoryTransactionPacket.TypeItemUse:
                HandleUseItem(
                    session,
                    player,
                    packet.UseActionType,
                    packet.BlockX,
                    packet.BlockY,
                    packet.BlockZ,
                    packet.BlockFace,
                    packet.HotbarSlot);
                break;
            case InventoryTransactionPacket.TypeItemUseOnActor:
                if (packet.ActorActionType == InventoryTransactionPacket.ActorAttack)
                {
                    PlayerVisibility.RelaySwingArm(
                        player,
                        session.Context.PlayerManager.SnapshotOnline(),
                        swingSource: "attack");
                }
                break;
        }
    }

}

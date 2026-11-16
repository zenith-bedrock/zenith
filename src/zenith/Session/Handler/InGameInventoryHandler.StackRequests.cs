using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Inventory;
using System.Collections.Generic;
using Zenith.Binary;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>ItemStackRequest: decoding the client's actions, deciding what they need, and rejecting what cannot be honoured.</summary>
partial class InGameSessionHandler
{
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
                else if (a.Kind is InventoryStackActionKind.CraftRecipe or InventoryStackActionKind.CraftAuto) hasRecipe = true;
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
            var hasSession = player.Windows.TryGetInventoryAdmissionSession(out var admittedSession);
            var craftingTarget = hasSession && admittedSession.Target == OpenContainerSession.TargetKind.CraftingTable
                ? OpenContainerSession.TargetKind.CraftingTable : OpenContainerSession.TargetKind.PlayerInventory;
            if (!TryGetSessionRequirement(baked, craftingTarget, out var requiresSession, out var requiredTarget))
            {
                RejectIsr(session, player, request.RequestId);
                continue;
            }

            if (requiresSession)
            {
                // Bind to the session after earlier queued windows, including reserved generations
                // for opens not yet applied. Failed opens cannot redirect this action to an old view.
                if (!hasSession || (requiredTarget is not null && admittedSession.Target != requiredTarget))
                {
                    RejectIsr(session, player, request.RequestId);
                    continue;
                }
                expectedOpenContainerGeneration = admittedSession.Generation;
            }

            var intent = InventoryStackIntent.Create(
                request.RequestId,
                baked.ToArray(),
                expectedOpenContainerGeneration,
                requiresSession,
                requiredTarget);
            if (!player.Intents.SubmitInventoryStack(intent))
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

        if (action.ActionType == ItemStackRequestPacket.ActionCraftRecipeAuto)
        {
            if (action.CraftTimes == 0)
            {
                baked = default;
                return false;
            }

            baked = InventoryStackAction.CraftAuto(action.RecipeNetId, action.CraftTimes);
            return true;
        }

        if (action.ActionType == ItemStackRequestPacket.ActionCreate)
        {
            baked = InventoryStackAction.CreateOutput(action.ResultSlot);
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
        OpenContainerSession.TargetKind craftingTarget,
        out bool requiresSession,
        out OpenContainerSession.TargetKind? requiredTarget)
    {
        requiresSession = false;
        requiredTarget = null;

        foreach (var action in actions)
        {
            if (action.Kind is InventoryStackActionKind.CraftRecipe or
                InventoryStackActionKind.CraftAuto or
                InventoryStackActionKind.CraftCreative or
                InventoryStackActionKind.Create or
                InventoryStackActionKind.Destroy)
            {
                if (!TryRequireTarget(craftingTarget, ref requiresSession, ref requiredTarget))
                    return false;
            }

            if (!TryApplyReferenceRequirement(action.From, craftingTarget, ref requiresSession, ref requiredTarget) ||
                !TryApplyReferenceRequirement(action.To, craftingTarget, ref requiresSession, ref requiredTarget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryApplyReferenceRequirement(
        in InventorySlotReference reference,
        OpenContainerSession.TargetKind craftingTarget,
        ref bool requiresSession,
        ref OpenContainerSession.TargetKind? requiredTarget)
    {
        return reference.Area switch
        {
            InventorySlotArea.OpenContainer =>
                TryRequireTarget(OpenContainerSession.TargetKind.Chest, ref requiresSession, ref requiredTarget),
            InventorySlotArea.CraftGrid =>
                TryRequireTarget(OpenContainerSession.TargetKind.PlayerInventory, ref requiresSession, ref requiredTarget),
            InventorySlotArea.TableCraftGrid =>
                TryRequireTarget(OpenContainerSession.TargetKind.CraftingTable, ref requiresSession, ref requiredTarget),
            InventorySlotArea.CraftResult =>
                TryRequireTarget(craftingTarget, ref requiresSession, ref requiredTarget),
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

    private static void RejectIsr(
        NetworkSession session,
        Player.Player player,
        int requestId,
        [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        // The call site's line says which gate refused the request — the wire only ever sees a bare error status.
        session.Context.Logger.Debug($"ItemStackRequest {requestId} from {player.Username} refused (InGameInventoryHandler.cs:{line}).");
        session.Protocol.Inventory.SendItemStackResponseError(requestId);
        session.Protocol.Inventory.ResyncActiveInventoryView(player);
    }
}

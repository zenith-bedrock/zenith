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

                if (!InventoryContainerMap.TryMap(action.Source.Container.ContainerId, action.Source.Slot, out var srcFlat) ||
                    !InventoryContainerMap.TryMap(action.Destination.Container.ContainerId, action.Destination.Slot, out var dstFlat))
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
                    baked.Add(InventoryStackAction.Swap(srcFlat, dstFlat, srcWire, dstWire));
                else
                    baked.Add(InventoryStackAction.Transfer(srcFlat, dstFlat, action.Count, srcWire, dstWire));
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

            var intent = InventoryStackIntent.Create(request.RequestId, baked.ToArray());
            if (!player.SubmitInventoryStack(intent))
            {
                session.Context.Logger.Debug($"Dropped ISR from {player.Username}: inventory-stack queue full.");
                RejectIsr(session, player, request.RequestId);
            }
        }
    }

    private static void RejectIsr(NetworkSession session, Player.Player player, int requestId)
    {
        session.Protocol.Inventory.SendItemStackResponseError(requestId);
        session.Protocol.Inventory.SendInventoryContent(player.Inventory);
        session.Protocol.Inventory.SendUiInventoryContent(player);
        if (player.OpenChest is { } chest)
            session.Protocol.Inventory.SendChestContent(session.Context.World.Chests, chest);
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

using System.Collections.Generic;
using Zenith.Gameplay;
using Zenith.Raknet.Stream;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>
/// Handler in-game. AuthInput/chat/blocos/ISR só registram intenção ou transmitem same-session —
/// posição/blocos/inventário finais no GameLoop.
/// </summary>
class InGameSessionHandler : ISessionHandler
{
    public void OnEnable(NetworkSession session)
    {
        if (session.Player is not null)
            session.Player.IsInGame = true;
        session.Context.Logger.Info($"{session.Player?.Username} is now in-game.");
    }

    public void OnDisable(NetworkSession session)
    {
        if (session.Player is not null)
            session.Player.IsInGame = false;
    }

    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        switch (header.Id)
        {
            case (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET:
                HandleAuthInput(session, ref stream);
                return true;

            case (int)ProtocolInfo.TEXT_PACKET:
                HandleText(session, ref stream);
                return true;

            case (int)ProtocolInfo.PLAYER_ACTION_PACKET:
                HandlePlayerAction(session, ref stream);
                return true;

            case (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET:
                HandleInventoryTransaction(session, ref stream);
                return true;

            case (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET:
                HandleItemStackRequest(session, ref stream);
                return true;

            case (int)ProtocolInfo.REQUEST_CHUNK_RADIUS_PACKET:
                HandleRequestChunkRadius(session, ref stream);
                return true;

            case (int)ProtocolInfo.MOVE_PLAYER_PACKET:
                return true;

            case (int)ProtocolInfo.MOB_EQUIPMENT_PACKET:
                HandleMobEquipment(session, ref stream);
                return true;

            case (int)ProtocolInfo.INTERACT_PACKET:
                HandleInteract(session, ref stream);
                return true;

            case (int)ProtocolInfo.CONTAINER_CLOSE_PACKET:
                HandleContainerClose(session, ref stream);
                return true;

            case (int)ProtocolInfo.REQUEST_ABILITY_PACKET:
                HandleRequestAbility(session, ref stream);
                return true;

            case (int)ProtocolInfo.DISCONNECT_PACKET:
                HandleDisconnect(session, ref stream);
                return true;

            case (int)ProtocolInfo.COMMAND_REQUEST_PACKET:
                HandleCommandRequest(session, ref stream);
                return true;

            case (int)ProtocolInfo.ANIMATE_PACKET:
            case (int)ProtocolInfo.LEVEL_SOUND_EVENT_PACKET:
            case (int)ProtocolInfo.EMOTE_LIST_PACKET:
            case (int)ProtocolInfo.SERVERBOUND_LOADING_SCREEN_PACKET:
                return true;

            default:
                return false;
        }
    }

    private static void HandleInteract(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<InteractPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;
        if (packet.Action != InteractPacket.ActionOpenInventory) return;

        session.Protocol.Inventory.SendContainerOpen(
            (int)MathF.Floor(player.PositionX),
            (int)MathF.Floor(player.PositionY),
            (int)MathF.Floor(player.PositionZ));
    }

    private static void HandleContainerClose(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<ContainerClosePacket>(ref stream);
        var player = session.Player;
        if (player is not null)
            player.OpenChest = null;
        session.Protocol.Inventory.SendContainerClose(packet.WindowId, packet.WindowType);
    }

    private static void HandleItemStackRequest(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<ItemStackRequestPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        foreach (var request in packet.Requests)
        {
            if (!request.AllSupported || request.Actions.Length == 0)
            {
                RejectIsr(session, player, request.RequestId);
                continue;
            }

            uint? craftNetId = null;
            uint? craftCreativeNetId = null;
            byte craftCreativeTimes = 0;
            var baked = new List<InventoryStackAction>(request.Actions.Length);
            var mapOk = true;
            foreach (var action in request.Actions)
            {
                if (action.ActionType == ItemStackRequestPacket.ActionCraftRecipe)
                {
                    craftNetId = action.RecipeNetId;
                    continue;
                }

                if (action.ActionType == ItemStackRequestPacket.ActionCraftCreative)
                {
                    craftCreativeNetId = action.CreativeNetId;
                    craftCreativeTimes = action.CraftTimes;
                    continue;
                }

                if (action.ActionType == ItemStackRequestPacket.ActionCraftResultsDeprecated ||
                    action.ActionType == ItemStackRequestPacket.ActionConsume ||
                    action.ActionType == ItemStackRequestPacket.ActionCreate)
                    continue;

                if (!InventoryContainerMap.TryMap(action.Source.Container.ContainerId, action.Source.Slot, out var from) ||
                    !InventoryContainerMap.TryMap(action.Destination.Container.ContainerId, action.Destination.Slot, out var to))
                {
                    mapOk = false;
                    break;
                }

                if (action.ActionType == ItemStackRequestPacket.ActionSwap)
                    baked.Add(InventoryStackAction.Swap(from, to));
                else
                    baked.Add(InventoryStackAction.Transfer(from, to, action.Count));
            }

            if (!mapOk)
            {
                RejectIsr(session, player, request.RequestId);
                continue;
            }

            InventoryStackIntent intent;
            if (craftNetId is not null && craftCreativeNetId is not null)
            {
                RejectIsr(session, player, request.RequestId);
                continue;
            }

            if (craftCreativeNetId is { } creativeId)
            {
                if (player.GameMode != GameMode.Creative ||
                    craftCreativeTimes == 0 ||
                    !session.Context.Creative.TryGet(creativeId, out _, out _))
                {
                    RejectIsr(session, player, request.RequestId);
                    continue;
                }

                intent = InventoryStackIntent.CreateCraftCreative(
                    request.RequestId, creativeId, craftCreativeTimes);
            }
            else if (craftNetId is { } rid)
            {
                if (!session.Context.Recipes.TryGet(rid, out _, out _, out _))
                {
                    RejectIsr(session, player, request.RequestId);
                    continue;
                }

                intent = InventoryStackIntent.CreateCraft(request.RequestId, rid);
            }
            else if (baked.Count == 0)
            {
                RejectIsr(session, player, request.RequestId);
                continue;
            }
            else
                intent = InventoryStackIntent.Create(request.RequestId, baked.ToArray());

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
        if (player.OpenChest is { } chest)
            session.Protocol.Inventory.SendChestContent(session.Context.World.Chests, chest.X, chest.Y, chest.Z);
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

    private static void HandleRequestChunkRadius(NetworkSession session, ref BinaryStream stream)
    {
        var request = DataPacket.From<RequestChunkRadiusPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        var cap = session.Context.Config.World.SpawnChunkRadius;
        var radius = Math.Min(request.Radius, cap);
        player.Chunks.Radius = radius;
        session.Protocol.World.SendChunkRadiusUpdated(radius);
        session.Context.Logger.Debug($"In-game chunk radius updated for {player.Username}: {radius}");
    }

    private static void HandleText(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<TextPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        if (packet.Type != TextPacket.TypeChat) return;
        if (packet.Message.StartsWith('/')) return;

        if (!session.Protocol.Chat.TryAcceptOutboundChat(player.Uuid, packet.Message, out var message))
        {
            session.Context.Logger.Debug($"Chat rejected from {player.Username} (rate/size/empty).");
            return;
        }

        player.SubmitChat(message);
    }

    private static void HandleAuthInput(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<PlayerAuthInputPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        var input = MovementInputState.From(
            packet.PositionX,
            packet.PositionY,
            packet.PositionZ,
            packet.Pitch,
            packet.Yaw);

        if (!input.IsSecure())
        {
            session.Context.Logger.Warning($"Rejected AuthInput from {player.Username}: non-finite floats.");
            return;
        }

        player.SubmitMovementInput(input);

        if (packet.ItemInteraction is { } useItem)
            HandleUseItemInteraction(session, player, useItem);

        foreach (var action in packet.BlockActions)
        {
            switch (action.Action)
            {
                case PlayerAuthInputPacket.ActionStartBreak:
                case PlayerAuthInputPacket.ActionContinueDestroy:
                case PlayerAuthInputPacket.ActionCrackBreak:
                    HandleBreakProgress(
                        session, player, action.Action, action.BlockX, action.BlockY, action.BlockZ);
                    break;
                case PlayerAuthInputPacket.ActionAbortBreak:
                    if (player.HasBreakTarget)
                        BlockCrackFanout.Stop(
                            session.Context.PlayerManager,
                            session,
                            player.BreakTargetX,
                            player.BreakTargetY,
                            player.BreakTargetZ);
                    player.AbortBreak();
                    break;
                case PlayerAuthInputPacket.ActionPredictDestroy:
                case PlayerActionPacket.ActionCreativeDestroy:
                    session.Context.Logger.Debug(
                        $"AuthInput break from {player.Username}: action={action.Action} @ {action.BlockX},{action.BlockY},{action.BlockZ}");
                    TrySubmitBreak(player, action.BlockX, action.BlockY, action.BlockZ);
                    break;
            }
        }
    }

    private static void HandlePlayerAction(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<PlayerActionPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        if (packet.Action is not (PlayerActionPacket.ActionCreativeDestroy or PlayerActionPacket.ActionPredictDestroy))
        {
            if (packet.Action is not (PlayerActionPacket.ActionStartItemUseOn or PlayerActionPacket.ActionStopItemUseOn))
            {
                session.Context.Logger.Debug(
                    $"PlayerAction ignored from {player.Username}: action={packet.Action}");
            }
            return;
        }

        session.Context.Logger.Debug(
            $"PlayerAction break from {player.Username}: action={packet.Action} @ {packet.BlockX},{packet.BlockY},{packet.BlockZ}");
        TrySubmitBreak(player, packet.BlockX, packet.BlockY, packet.BlockZ);
    }

    private static void HandleInventoryTransaction(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<InventoryTransactionPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

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
        }
    }

    private static void HandleUseItemInteraction(
        NetworkSession session,
        Player.Player player,
        in AuthItemInteraction use)
    {
        var face = use.Face is >= 0 and <= 255 ? (byte)use.Face : (byte)0;
        HandleUseItem(
            session,
            player,
            use.ActionType,
            use.BlockX,
            use.BlockY,
            use.BlockZ,
            face,
            use.HotbarSlot);
    }

    private static void HandleUseItem(
        NetworkSession session,
        Player.Player player,
        int useActionType,
        int blockX,
        int blockY,
        int blockZ,
        byte blockFace,
        int hotbarSlot)
    {
        if (!PlayerInventory.IsValidHotbarSlot(hotbarSlot))
        {
            session.Context.Logger.Debug($"Rejected UseItem: hotbar {hotbarSlot}");
            return;
        }

        player.SelectedHotbarSlot = hotbarSlot;

        if (useActionType == InventoryTransactionPacket.UseDestroyBlock)
        {
            session.Context.Logger.Debug(
                $"UseItem destroy from {player.Username} @ {blockX},{blockY},{blockZ}");
            TrySubmitBreak(player, blockX, blockY, blockZ);
            return;
        }

        if (useActionType != InventoryTransactionPacket.UseClickBlock) return;

        var stack = player.Inventory.Get(hotbarSlot);
        var clicked = session.Context.World.GetBlock(blockX, blockY, blockZ);

        // Empty hand on chest → open UI (§28). Sneak+place não modelado ainda.
        if (Blocks.IsChest(clicked) && (stack.IsEmpty || stack.Count <= 0))
        {
            OpenChestUi(session, player, blockX, blockY, blockZ);
            return;
        }

        if (!PlayerInventory.IsValidStackCount(stack.Count) || stack.Count <= 0)
        {
            session.Context.Logger.Debug($"Rejected place: invalid/empty stack count {stack.Count}");
            return;
        }

        var runtimeId = stack.RuntimeId;
        if (runtimeId == World.World.AirRuntimeId) return;
        // Facing policy on live place only — BlockSystem / SubmitBlockEdit with raw Blocks.Chest stay as-given.
        if (Blocks.IsChest(runtimeId))
            runtimeId = ChestFacing.RuntimeIdFromYaw(player.Yaw);

        var (tx, ty, tz) = FaceOffset(blockX, blockY, blockZ, blockFace);
        var intent = BlockEditIntent.Set(tx, ty, tz, runtimeId, hotbarSlot);
        if (!intent.IsInWorldBounds())
        {
            session.Context.Logger.Debug($"Rejected place OOB from {player.Username}");
            return;
        }

        if (!player.SubmitBlockEdit(intent))
            session.Context.Logger.Debug($"Dropped place from {player.Username}: block-edit queue full.");
        else
            session.Context.Logger.Debug(
                $"Place queued from {player.Username} @ {tx},{ty},{tz} rid={runtimeId}");
    }

    private static void HandleRequestAbility(NetworkSession session, ref BinaryStream stream)
    {
        var packet = new RequestAbilityPacket();
        packet.Decode(ref stream);

        if (packet.Ability != AbilityBits.Flying)
            return;

        var player = session.Player;
        if (player is null) return;

        if (player.GameMode != GameMode.Creative)
            return;

        session.Protocol.Entity.SendLocalAbilities(
            player.RuntimeId,
            AbilityBits.WireGameModeCreative,
            flying: packet.BoolValue);
    }

    private static void HandleDisconnect(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<DisconnectPacket>(ref stream);
        session.Context.Logger.Info(
            $"DisconnectPacket from {session.Player?.Username ?? session.RakSession.EndPoint.ToString()}: " +
            $"reason={packet.Reason}, message={packet.Message}");
        session.Disconnect();
    }

    private static void HandleCommandRequest(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<CommandRequestPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        player.SubmitCommand(packet.Command, packet.OriginData);
    }

    private static void OpenChestUi(NetworkSession session, Player.Player player, int x, int y, int z)
    {
        session.Context.World.Chests.Ensure(x, y, z);
        player.OpenChest = (x, y, z);
        var inv = session.Protocol.Inventory;
        inv.SendChestOpen(x, y, z);
        inv.SendChestContent(session.Context.World.Chests, x, y, z);
        inv.SendInventoryContent(player.Inventory);
        session.Context.Logger.Debug($"Chest open for {player.Username} @ {x},{y},{z}");
    }

    /// <summary>
    /// Survival: start_break / new-cell continue / crack_break without target begins dig + crack.
    /// Same-cell continue_destroy / crack_break keep the timer (do not re-StartCrack — resets client stage).
    /// Creative InstantBuild: no crack / no dig timer (destroy arrives as creative_destroy).
    /// </summary>
    private static void HandleBreakProgress(
        NetworkSession session,
        Player.Player player,
        int action,
        int x,
        int y,
        int z)
    {
        if (player.GameMode == GameMode.Creative)
            return;

        if (player.IsBreakTarget(x, y, z))
            return;

        if (player.HasBreakTarget)
            BlockCrackFanout.Stop(
                session.Context.PlayerManager,
                session,
                player.BreakTargetX,
                player.BreakTargetY,
                player.BreakTargetZ);

        var block = session.Context.World.GetBlock(x, y, z);
        var need = Blocks.BreakTicks(block);
        player.BeginBreak(x, y, z, session.Context.Clock.CurrentTick, need);

        // Always cue crack for breakable cells — CrackProgressMax = one-tick snap for soft blocks.
        BlockCrackFanout.Start(session.Context.PlayerManager, session, x, y, z, need);

        var label = action switch
        {
            PlayerAuthInputPacket.ActionStartBreak => "start_break",
            PlayerAuthInputPacket.ActionCrackBreak => "crack_break",
            _ => "continue_destroy"
        };
        session.Context.Logger.Debug(
            $"AuthInput {label} from {player.Username} @ {x},{y},{z} need={need} ticks");
    }

    private static void TrySubmitBreak(Player.Player player, int x, int y, int z)
    {
        var intent = BlockEditIntent.Set(x, y, z, World.World.AirRuntimeId);
        if (!intent.IsInWorldBounds()) return;
        if (!player.SubmitBlockEdit(intent))
            player.Session.Context.Logger.Debug($"Dropped break from {player.Username}: block-edit queue full.");
        else
            player.Session.Context.Logger.Debug(
                $"Break queued from {player.Username} @ {x},{y},{z}");
    }

    private static (int X, int Y, int Z) FaceOffset(int x, int y, int z, byte face) => face switch
    {
        0 => (x, y - 1, z),
        1 => (x, y + 1, z),
        2 => (x, y, z - 1),
        3 => (x, y, z + 1),
        4 => (x - 1, y, z),
        5 => (x + 1, y, z),
        _ => (x, y, z)
    };
}

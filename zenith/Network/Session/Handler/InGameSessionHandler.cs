using Zenith.Raknet.Stream;
using Zenith.Network.Packets;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Network.Session.Handler;

/// <summary>
/// Handler in-game. AuthInput/chat/blocos só registram intenção ou transmitem —
/// posição/blocos finais no GameLoop.
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

            case (int)ProtocolInfo.MOVE_PLAYER_PACKET:
                return true;

            case (int)ProtocolInfo.MOB_EQUIPMENT_PACKET:
            case (int)ProtocolInfo.INTERACT_PACKET:
            case (int)ProtocolInfo.EMOTE_LIST_PACKET:
            case (int)ProtocolInfo.SERVERBOUND_LOADING_SCREEN_PACKET:
                return true;

            default:
                return false;
        }
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

        foreach (var peer in session.Context.PlayerManager.Online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.Chat.SendChat(player.Username, message);
        }
    }

    private static void HandlePlayerAction(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<PlayerActionPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        if (packet.Action is not (PlayerActionPacket.ActionCreativeDestroy or PlayerActionPacket.ActionPredictDestroy))
            return;

        TrySubmitBreak(player, packet.BlockX, packet.BlockY, packet.BlockZ);
    }

    private static void HandleInventoryTransaction(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<InventoryTransactionPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;
        if (packet.TransactionType != InventoryTransactionPacket.TypeUseItem) return;

        if (packet.HotbarSlot is < 0 or > 8)
        {
            session.Context.Logger.Debug($"Rejected InventoryTransaction: hotbar {packet.HotbarSlot}");
            return;
        }

        player.SelectedHotbarSlot = packet.HotbarSlot;

        if (packet.UseActionType == InventoryTransactionPacket.UseDestroyBlock)
        {
            TrySubmitBreak(player, packet.BlockX, packet.BlockY, packet.BlockZ);
            return;
        }

        if (packet.UseActionType != InventoryTransactionPacket.UseClickBlock) return;

        var (tx, ty, tz) = FaceOffset(packet.BlockX, packet.BlockY, packet.BlockZ, packet.BlockFace);
        var runtimeId = packet.HeldBlockRuntimeId != 0 ? packet.HeldBlockRuntimeId : player.HeldBlockRuntimeId;
        if (runtimeId == World.World.AirRuntimeId) return;

        var intent = BlockEditIntent.Set(tx, ty, tz, runtimeId);
        if (!intent.IsInWorldBounds())
        {
            session.Context.Logger.Debug($"Rejected place OOB from {player.Username}");
            return;
        }

        player.SubmitBlockEdit(intent);
    }

    private static void TrySubmitBreak(Player.Player player, int x, int y, int z)
    {
        var intent = BlockEditIntent.Set(x, y, z, World.World.AirRuntimeId);
        if (!intent.IsInWorldBounds()) return;
        player.SubmitBlockEdit(intent);
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

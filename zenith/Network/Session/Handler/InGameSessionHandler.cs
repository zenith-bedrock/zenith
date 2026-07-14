using Zenith.Raknet.Stream;
using Zenith.Network.Packets;
using Zenith.Player;

namespace Zenith.Network.Session.Handler;

/// <summary>
/// Handler in-game. AuthInput só registra <see cref="MovementInputState"/> —
/// posição final é aplicada no MovementSystem (GameLoop).
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

            case (int)ProtocolInfo.MOVE_PLAYER_PACKET:
                // Legado: engolir sem mutar gameplay neste PR (AuthInput é o caminho atual).
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
}

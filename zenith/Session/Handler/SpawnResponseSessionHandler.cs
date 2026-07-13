using Zenith.Raknet.Stream;
using Zenith.Network.Protocol;

namespace Zenith.Session.Handler;

/// <summary>
/// Estado entre "chunks publicados e PlayStatus(PLAYER_SPAWN) enviado" e "cliente confirmou
/// que terminou de entrar no jogo" (<see cref="SetLocalPlayerAsInitializedPacket"/>). Nesse
/// meio tempo o cliente já solta bastante spam de pacotes de controle/movimento mesmo sem
/// estar oficialmente in-game ainda; só ignora esses aqui até a confirmação chegar.
/// </summary>
class SpawnResponseSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        switch (header.Id)
        {
            case (int)ProtocolInfo.SET_LOCAL_PLAYER_AS_INITIALIZED_PACKET:
                HandleSetLocalPlayerAsInitialized(session, ref stream);
                return true;

            case (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET:
            case (int)ProtocolInfo.SERVERBOUND_LOADING_SCREEN_PACKET:
            case (int)ProtocolInfo.MOVE_PLAYER_PACKET:
            case (int)ProtocolInfo.MOB_EQUIPMENT_PACKET:
            case (int)ProtocolInfo.INTERACT_PACKET:
            case (int)ProtocolInfo.EMOTE_LIST_PACKET:
                return true;

            default:
                return false;
        }
    }

    private static void HandleSetLocalPlayerAsInitialized(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<SetLocalPlayerAsInitializedPacket>(ref stream);
        session.Context.Logger.Info($"{session.Player?.Username} finished spawning (actor {packet.ActorRuntimeId}), entering in-game phase.");
        session.SetHandler(new InGameSessionHandler());
    }
}

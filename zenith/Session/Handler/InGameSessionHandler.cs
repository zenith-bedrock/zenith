using Zenith.Raknet.Stream;
using Zenith.Network.Protocol;

namespace Zenith.Session.Handler;

/// <summary>
/// Handler final da sessão, ativo depois que o cliente confirma que terminou de spawnar
/// (<see cref="SetLocalPlayerAsInitializedPacket"/>). Ainda não sabe processar movimento,
/// inventário ou ações de verdade - só evita que os pacotes mais comuns nessa fase (input,
/// movimento, equipamento) poluam o log como "unhandled" enquanto esse estado de jogo não
/// existe. Cada TODO aqui vira sua própria feature conforme Player/World forem crescendo.
/// </summary>
class InGameSessionHandler : ISessionHandler
{
    public void OnEnable(NetworkSession session)
    {
        session.Context.Logger.Info($"{session.Player?.Username} is now in-game.");
    }

    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        switch (header.Id)
        {
            // TODO: sincronizar posição de verdade assim que Player tiver estado de mundo.
            case (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET:
            case (int)ProtocolInfo.MOVE_PLAYER_PACKET:
            // TODO: refletir item selecionado assim que existir inventário.
            case (int)ProtocolInfo.MOB_EQUIPMENT_PACKET:
            // TODO: tratar interação com entidades/blocos assim que existirem.
            case (int)ProtocolInfo.INTERACT_PACKET:
            case (int)ProtocolInfo.EMOTE_LIST_PACKET:
            case (int)ProtocolInfo.SERVERBOUND_LOADING_SCREEN_PACKET:
                return true;

            default:
                return false;
        }
    }
}

using Zenith.Raknet.Stream;
using Zenith.Network.Protocol;

namespace Zenith.Session.Handler;

/// <summary>
/// Negociação de resource packs. Ao receber STATUS_COMPLETED, manda o StartGamePacket e
/// troca pra <see cref="PreSpawnSessionHandler"/>.
/// </summary>
class ResourcePacksSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        if (header.Id == (int)ProtocolInfo.CLIENT_CACHE_STATUS_PACKET) return true;

        if (header.Id != (int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET) return false;

        var response = DataPacket.From<ResourcePackClientResponsePacket>(ref stream);
        session.Context.Logger.Debug($"ResourcePackClientResponsePacket: {response.Status}");

        switch (response.Status)
        {
            case ResourcePackClientResponsePacket.STATUS_HAVE_ALL_PACKS:
                session.SendDataPacket(new ResourcePackStackPacket
                {
                    MustAccept = false,
                    GameVersion = "1.26.33",
                    ExperimentsPreviouslyToggled = false,
                    HasEditorPacks = false
                });
                break;
            case ResourcePackClientResponsePacket.STATUS_COMPLETED:
                // PlayStatus(PLAYER_SPAWN) não é mandado aqui de propósito: mandar esse status
                // antes de qualquer chunk existir é o que fazia o cliente ficar preso na tela
                // de "Loading world" esperando terreno que nunca vinha. PreSpawnSessionHandler
                // manda esse status só depois de publicar chunks (mesmo que falsos) - ver lá.
                session.SendDataPacket(new StartGamePacket { LevelName = "world" });
                session.SetHandler(new PreSpawnSessionHandler());
                break;
        }

        return true;
    }
}

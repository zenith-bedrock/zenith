using Zenith.Raknet.Stream;
using Zenith.Network.Packets;

namespace Zenith.Network.Session.Handler;

/// <summary>
/// Negociação de resource packs. Ao receber STATUS_COMPLETED, manda StartGame via Protocol
/// e troca pra <see cref="PreSpawnSessionHandler"/>.
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
                session.Protocol.ResourcePacks.SendStack(
                    mustAccept: false,
                    gameVersion: "1.26.33",
                    experimentsPreviouslyToggled: false,
                    hasEditorPacks: false);
                break;
            case ResourcePackClientResponsePacket.STATUS_COMPLETED:
                // PlayStatus(PLAYER_SPAWN) não é mandado aqui de propósito: PreSpawn manda
                // esse status só depois de publicar chunks (mesmo que falsos).
                var player = session.Player!;
                session.Protocol.World.SendStartGame(
                    levelName: "world",
                    entityRuntimeId: player.RuntimeId,
                    x: player.PositionX,
                    y: player.PositionY,
                    z: player.PositionZ,
                    pitch: player.Pitch,
                    yaw: player.Yaw);
                session.SetHandler(new PreSpawnSessionHandler());
                break;
        }

        return true;
    }
}

using Zenith.Raknet.Stream;
using zenith.Network.Protocol;

namespace zenith.Session.Handler;

/// <summary>
/// Negociação de resource packs. Ao receber STATUS_COMPLETED, manda o StartGamePacket e
/// troca pra <see cref="PreSpawnSessionHandler"/>.
/// </summary>
class ResourcePacksSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, BinaryStream stream)
    {
        if (header.Id != (int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET) return false;

        var response = DataPacket.From<ResourcePackClientResponsePacket>(stream);
        Console.WriteLine($"ResourcePackClientResponsePacket: {response.Status}");

        switch (response.Status)
        {
            case ResourcePackClientResponsePacket.STATUS_HAVE_ALL_PACKS:
                session.SendDataPacket(new ResourcePackStackPacket
                {
                    MustAccept = false,
                    GameVersion = "1.21.51",
                    ExperimentsPreviouslyToggled = false,
                    HasEditorPacks = false
                });
                break;
            case ResourcePackClientResponsePacket.STATUS_COMPLETED:
                session.SendDataPacket(
                    new StartGamePacket { LevelName = "world" },
                    new PlayStatusPacket { Status = 3 }
                );
                session.SetHandler(new PreSpawnSessionHandler());
                break;
        }

        return true;
    }
}

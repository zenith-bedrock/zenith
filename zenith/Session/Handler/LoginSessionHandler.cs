using Zenith.Raknet.Stream;
using zenith.Network.Protocol;

namespace zenith.Session.Handler;

/// <summary>
/// Primeiro estado de toda conexão: negociação de network settings e login. Handler inicial
/// atribuído a toda <see cref="NetworkSession"/> nova. Ao final do login com sucesso, troca
/// pra <see cref="ResourcePacksSessionHandler"/>.
/// </summary>
class LoginSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, BinaryStream stream)
    {
        switch (header.Id)
        {
            case (int)ProtocolInfo.REQUEST_NETWORK_SETTINGS_PACKET:
                HandleRequestNetworkSettings(session, stream);
                return true;
            case (int)ProtocolInfo.LOGIN_PACKET:
                HandleLogin(session, stream);
                return true;
            default:
                return false;
        }
    }

    private static void HandleRequestNetworkSettings(NetworkSession session, BinaryStream stream)
    {
        var packet = DataPacket.From<RequestNetworkSettingsPacket>(stream);
        Console.WriteLine($"RequestNetworkSettingsPacket: {packet.ProtocolVersion}");

        // TODO: recusar a conexão aqui se packet.ProtocolVersion não for suportado,
        // em vez de deixar seguir com um protocolo desconhecido.

        var settings = new NetworkSettingsPacket
        {
            CompressionThreshold = 256,
            CompressionAlgorithm = PacketCompression.NONE,
            EnableClientThrottling = false,
            ClientThrottleThreshold = 0,
            ClientThrottleScalar = 0
        };

        session.SendDataPacket(Zenith.Raknet.RakNetSession.Priority.Normal, PacketCompression.NOT_PRESENT, settings);
    }

    private static void HandleLogin(NetworkSession session, BinaryStream stream)
    {
        var packet = DataPacket.From<LoginPacket>(stream);
        Console.WriteLine($"LoginPacket: {packet.Protocol}");

        // TODO: validar a chain JWT e extrair identidade (uuid, xuid, username).
        // É aqui que o Player vai ser criado assim que Player/PlayerManager existirem.

        var playStatus = new PlayStatusPacket { Status = 0 };
        var resourcePacksInfo = new ResourcePacksInfoPacket
        {
            MustAccept = true,
            HasAddons = false,
            HasScripts = false,
            WorldTemplateVersion = ""
        };

        session.SendDataPacket(playStatus, resourcePacksInfo);
        session.SetHandler(new ResourcePacksSessionHandler());
    }
}

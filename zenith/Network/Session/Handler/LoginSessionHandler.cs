using Zenith.Raknet.Stream;
using Zenith.Event;
using Zenith.Network.Packets;

namespace Zenith.Network.Session.Handler;

/// <summary>
/// Primeiro estado de toda conexão: negociação de network settings e login. Handler inicial
/// atribuído a toda <see cref="NetworkSession"/> nova. Ao final do login com sucesso, troca
/// pra <see cref="ResourcePacksSessionHandler"/>.
///
/// Parsing de identidade fica em <see cref="LoginIdentity"/> — este handler só orquestra o fluxo.
/// </summary>
class LoginSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        switch (header.Id)
        {
            case (int)ProtocolInfo.REQUEST_NETWORK_SETTINGS_PACKET:
                HandleRequestNetworkSettings(session, ref stream);
                return true;
            case (int)ProtocolInfo.LOGIN_PACKET:
                HandleLogin(session, ref stream);
                return true;
            default:
                return false;
        }
    }

    private static void HandleRequestNetworkSettings(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<RequestNetworkSettingsPacket>(ref stream);
        session.Context.Logger.Debug($"RequestNetworkSettingsPacket: {packet.ProtocolVersion}");

        // TODO: recusar a conexão aqui se packet.ProtocolVersion não for suportado,
        // em vez de deixar seguir com um protocolo desconhecido.

        session.CompressionAlgorithm = PacketCompression.ZLIB;
        session.Protocol.Login.SendNetworkSettings(
            compressionThreshold: 256,
            compressionAlgorithm: PacketCompression.ZLIB,
            enableClientThrottling: false,
            clientThrottleThreshold: 0,
            clientThrottleScalar: 0);
    }

    private static void HandleLogin(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<LoginPacket>(ref stream);
        session.Context.Logger.Debug($"LoginPacket: {packet.Protocol}");

        string username;
        try
        {
            username = LoginIdentity.ExtractDisplayName(packet.AuthInfo.Token);
        }
        catch (Exception ex)
        {
            session.Context.Logger.Warning($"Failed to parse identity chain: {ex.Message}");
            session.Disconnect();
            return;
        }

        // TODO: validar a assinatura da chain contra a chave raiz da Mojang antes de confiar
        // no displayName - hoje qualquer cliente pode se declarar com qualquer nome/uuid.
        // Suficiente pra desenvolvimento local, não serve pra produção exposta.

        var player = new Zenith.Player.Player(username, session, session.Context.PlayerManager.AllocateRuntimeId());
        if (!session.Context.PlayerManager.TryAdd(player))
        {
            session.Context.Logger.Warning($"Rejected login: '{username}' already online.");
            session.Disconnect();
            return;
        }

        session.Player = player;
        session.Context.EventBus.Publish(new PlayerLoginEvent(player));

        session.Protocol.Login.SendLoginSuccess();
        session.Protocol.ResourcePacks.SendInfo(
            mustAccept: true,
            hasAddons: false,
            hasScripts: false,
            worldTemplateVersion: "");
        session.SetHandler(new ResourcePacksSessionHandler());
    }
}

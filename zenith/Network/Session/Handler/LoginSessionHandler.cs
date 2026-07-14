using Zenith.Raknet.Stream;
using Zenith.Event;
using Zenith.Network.Packets;

namespace Zenith.Network.Session.Handler;

/// <summary>
/// Primeiro estado de toda conexão: negociação de network settings e login.
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

        LoginIdentity.ParsedIdentity identity;
        try
        {
            if (!string.IsNullOrWhiteSpace(packet.AuthInfo.Certificate))
                LoginIdentity.ValidateIdentityChain(packet.AuthInfo.Certificate);

            identity = LoginIdentity.ParseIdentityToken(packet.AuthInfo.Token);
            identity = LoginIdentity.AttachClientSkin(identity, packet.ClientDataJwt);
        }
        catch (Exception ex)
        {
            session.Context.Logger.Warning($"Failed to parse/validate identity: {ex.Message}");
            session.Disconnect();
            return;
        }

        var player = new Zenith.Player.Player(
            identity.DisplayName,
            session,
            session.Context.PlayerManager.AllocateRuntimeId(),
            identity.Uuid)
        {
            SkinRgba = identity.SkinRgba,
            SkinWidth = identity.SkinWidth,
            SkinHeight = identity.SkinHeight
        };

        if (!session.Context.PlayerManager.TryAdd(player))
        {
            session.Context.Logger.Warning($"Rejected login: '{identity.DisplayName}' already online.");
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

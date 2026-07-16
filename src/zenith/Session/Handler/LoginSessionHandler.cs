using Zenith.Raknet.Stream;
using Zenith.Event;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Server;
using Zenith.Session;

namespace Zenith.Session.Handler;

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
            compressionThreshold: (short)session.Context.Config.Network.CompressionThreshold,
            compressionAlgorithm: PacketCompression.ZLIB,
            enableClientThrottling: false,
            clientThrottleThreshold: 0,
            clientThrottleScalar: 0);

        if (!TryAcceptProtocol(session, packet.ProtocolVersion, stage: "RequestNetworkSettings"))
            return;
    }

    private static void HandleLogin(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<LoginPacket>(ref stream);
        session.Context.Logger.Debug($"LoginPacket: {packet.Protocol}");

        if (!TryAcceptProtocol(session, packet.Protocol, stage: "Login"))
            return;

        var auth = session.Context.Config.Auth;
        var authType = packet.AuthInfo.AuthenticationType;
        if (authType == LoginPacket.AuthenticationInfo.TypeGuest)
        {
            session.Context.Logger.Warning("Rejected login: guest authentication is not supported.");
            session.Disconnect();
            return;
        }

        if (authType == LoginPacket.AuthenticationInfo.TypeFull && !auth.AllowsXbox)
        {
            session.Context.Logger.Warning(
                "Rejected login: AuthenticationType FULL but auth.accept does not include xbox.");
            session.Disconnect();
            return;
        }

        if (authType == LoginPacket.AuthenticationInfo.TypeSelfSigned && !auth.AllowsSelfSigned)
        {
            session.Context.Logger.Warning(
                "Rejected login: AuthenticationType SELF_SIGNED but auth.accept does not include self-signed.");
            session.Disconnect();
            return;
        }

        LoginIdentity.ParsedIdentity identity;
        try
        {
            if (!string.IsNullOrWhiteSpace(packet.AuthInfo.Certificate))
                LoginIdentity.ValidateIdentityChain(packet.AuthInfo.Certificate, auth.RequireStrictXbox);

            identity = LoginIdentity.ParseIdentityToken(
                packet.AuthInfo.Token, packet.AuthInfo.Certificate, packet.ClientDataJwt, auth);
            identity = LoginIdentity.AttachClientSkin(identity, packet.ClientDataJwt);
        }
        catch (Exception ex)
        {
            session.Context.Logger.Warning($"Failed to parse/validate identity: {ex.Message}");
            session.Disconnect();
            return;
        }

        if (!identity.IdentityStable)
        {
            session.Context.Logger.Warning(
                $"Login '{identity.DisplayName}': no stable identity (identity/xid/chain) — " +
                $"using ephemeral uuid {identity.Uuid:D}; inventory will not persist across rejoins.");
        }

        var gameMode = Zenith.Player.GameModeConfig.FromConfig(session.Context.Config.Server.Gamemode);
        var player = new Zenith.Player.Player(
            identity.DisplayName,
            session,
            session.Context.PlayerManager.AllocateRuntimeId(),
            identity.Uuid,
            gameMode)
        {
            SkinRgba = identity.SkinRgba,
            SkinWidth = identity.SkinWidth,
            SkinHeight = identity.SkinHeight
        };

        if (!session.Context.PlayerManager.TryAdd(player))
        {
            var existing = session.Context.PlayerManager.Get(identity.DisplayName);
            if (existing is not null)
            {
                session.Context.Logger.Warning(
                    $"Displacing already-online player '{identity.DisplayName}' for reconnect.");
                existing.Session.Disconnect();
            }

            if (!session.Context.PlayerManager.TryAdd(player))
            {
                session.Context.Logger.Warning($"Rejected login: '{identity.DisplayName}' already online.");
                session.Disconnect();
                return;
            }
        }

        session.Player = player;
        session.RakSession.HasGameIdentity = true;
        var loaded = session.Context.World.TryLoadInventory(player.Uuid, player.Inventory);
        session.Context.Logger.Info(
            loaded
                ? $"Inventory load hit for '{player.Username}' uuid={player.Uuid:D}"
                : $"Inventory load miss for '{player.Username}' uuid={player.Uuid:D} (starter/empty bag)");
        session.Context.EventBus.Publish(new PlayerLoginEvent(player));

        session.Protocol.Login.SendLoginSuccess();
        session.Protocol.ResourcePacks.SendInfo(
            mustAccept: true,
            hasAddons: false,
            hasScripts: false,
            worldTemplateVersion: "");
        session.SetHandler(new ResourcePacksSessionHandler());
    }

    /// <summary>
    /// Evaluate → Publish <see cref="ProtocolNegotiateEvent"/> → reject PlayStatus + disconnect if !Accepted.
    /// </summary>
    private static bool TryAcceptProtocol(NetworkSession session, int clientProtocol, string stage)
    {
        var serverProtocol = ServerIdentity.ProtocolVersion;
        var outcome = ProtocolGate.Evaluate(clientProtocol, serverProtocol);
        var rejectStatus = ProtocolGate.RejectPlayStatus(outcome);
        var negotiate = new ProtocolNegotiateEvent(
            clientProtocol,
            serverProtocol,
            accepted: outcome == ProtocolGate.Outcome.Accepted,
            rejectPlayStatus: rejectStatus);

        session.Context.EventBus.Publish(negotiate);

        if (negotiate.Accepted)
            return true;

        session.Context.Logger.Warning(
            $"Rejected {stage}: client protocol {clientProtocol} vs server {serverProtocol} " +
            $"(PlayStatus={negotiate.RejectPlayStatus}).");
        session.Protocol.Login.SendIncompatibleProtocol(negotiate.RejectPlayStatus);
        session.FlushAndDisconnect();
        return false;
    }
}

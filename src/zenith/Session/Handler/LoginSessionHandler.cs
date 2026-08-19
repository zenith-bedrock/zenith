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
                $"using ephemeral uuid {identity.Uuid:D}; inventory/playerdata will not persist across rejoins.");
        }

        // Join skin = ClientData full SerializedSkin (DF parseSkin / PM ClientDataToSkinDataHelper).
        // Mid-game PlayerSkin is secondary; nobody needs to change skin for peers to see it.
        if (ClientSkinParser.TryParse(packet.ClientDataJwt, out var joinSkin))
        {
            session.Skin = joinSkin;
            session.SkinTrusted = ClientSkinParser.IsTrusted(packet.ClientDataJwt);
            if (joinSkin.TryGetClassicRgba(out var rgba, out var sw, out var sh))
            {
                identity = identity with { SkinRgba = rgba, SkinWidth = sw, SkinHeight = sh };
            }
        }
        else
        {
            identity = LoginIdentity.AttachClientSkin(identity, packet.ClientDataJwt);
        }

        session.Profile = ClientProfileParser.Parse(
            packet.AuthInfo.Token,
            packet.AuthInfo.Certificate,
            packet.ClientDataJwt);

        var gameMode = Zenith.Player.GameModeConfig.FromConfig(session.Context.Config.Server.Gamemode);
        var player = new Zenith.Player.Player(
            identity.DisplayName,
            session,
            session.Context.PlayerManager.AllocateRuntimeId(),
            identity.Uuid,
            gameMode,
            identity.IdentityStable)
        {
            SkinRgba = identity.SkinRgba,
            SkinWidth = identity.SkinWidth,
            SkinHeight = identity.SkinHeight
        };

        // Deliberately NOT PlayerManager.TryAdd yet — that's what makes this player visible to
        // GameLoop's Online snapshot (PlayerManager.Online/FillOnline). Populate everything below
        // (inventory, armor, position, gamemode, vitals) on this still-private object first, so a
        // concurrent GameLoop tick can never observe a half-initialized player. TryAdd moves to
        // just before PlayerLoginEvent publishes, once the object is actually tick-ready.
        var loaded = session.Context.World.TryLoadInventory(player.Uuid, player.Inventory);
        session.Context.Logger.Info(
            loaded
                ? $"Inventory load hit for '{player.Username}' uuid={player.Uuid:D}"
                : $"Inventory load miss for '{player.Username}' uuid={player.Uuid:D} (starter/empty bag)");
        session.Context.World.TryLoadArmor(player.Uuid, player.Inventory);

        if (session.Context.World.TryLoadPlayerData(
                player.Uuid, out var px, out var py, out var pz, out var yaw, out var pitch, out var savedMode,
                out var savedLevel, out var savedPoints,
                out var savedHealth, out var savedHunger, out var savedSaturation, out var savedExhaustion))
        {
            player.PositionX = px;
            player.PositionY = py;
            player.PositionZ = pz;
            player.Yaw = yaw;
            player.Pitch = pitch;
            player.HeadYaw = yaw;
            player.SetGameMode(savedMode);
            player.SetExperience(savedLevel, savedPoints);
            player.HydrateVitals(savedHealth, savedHunger, savedSaturation, savedExhaustion);
            session.Context.Logger.Info(
                $"Playerdata load hit for '{player.Username}' @ {px:F1},{py:F1},{pz:F1} mode={savedMode} xp={savedLevel}/{savedPoints} " +
                $"hp={savedHealth:F1} hunger={savedHunger:F1}");

            // Flat-era pd: (Y≈-60) into noise hills → buried solid; client never leaves loading (PM/DF stand on surface).
            if (session.Context.World.TryHealSpawnFeet(player))
            {
                session.Context.Logger.Info(
                    $"Spawn heal for '{player.Username}': Y {py:F1} → {player.PositionY:F0} (clear feet at XZ)");
                session.Context.World.PersistPlayerData(player);
            }
        }
        else
        {
            // First join: stand on base terrain at world origin (noise-aware; ADR §63).
            player.PositionX = 0f;
            player.PositionY = session.Context.World.SampleSpawnFeetY(0, 0);
            player.PositionZ = 0f;
            session.Context.Logger.Info(
                $"Playerdata load miss for '{player.Username}' uuid={player.Uuid:D} " +
                $"(terrain spawn Y={player.PositionY:F0} / config mode)");
        }

        // Player is fully populated now — safe to make it visible to the GameLoop for the first time.
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

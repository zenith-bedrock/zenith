using Zenith.Binary;
using Zenith.Event;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Server;
using Zenith.Session;
using Zenith.Network;

namespace Zenith.Session.Handler;

/// <summary>
/// Primeiro estado de toda conexão: negociação de network settings e login.
/// </summary>
class LoginSessionHandler : ISessionHandler
{
    /// <summary>
    /// Issuer-authenticated SDP identity must match FULL login exactly. Offline SDP assertions and
    /// SELF_SIGNED game logins use independent keys/tokens and cannot establish an Xbox identity.
    /// </summary>
    internal static bool MatchesTransportAuthentication(TransportIdentity? identity, int authenticationType, string loginToken)
    {
        if (authenticationType == LoginPacket.AuthenticationInfo.TypeFull)
            return identity?.Authenticated == true &&
                   string.Equals(identity.GameServerToken, loginToken, StringComparison.Ordinal);
        return authenticationType == LoginPacket.AuthenticationInfo.TypeSelfSigned && identity?.Authenticated != true;
    }

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
        LoginPacket packet;
        try { packet = DataPacket.From<LoginPacket>(ref stream); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            session.Context.Logger.Debug($"Login payload rejected: {ex.GetType().Name}.");
            session.Disconnect();
            return;
        }
        session.Context.Logger.Debug($"LoginPacket: {packet.Protocol}");

        if (!TryAcceptProtocol(session, packet.Protocol, stage: "Login"))
            return;

        var authType = packet.AuthInfo.AuthenticationType;
        if (!IsAuthenticationAllowed(session, packet, authType) ||
            !TryResolveIdentity(session, packet, authType, out var identity))
        {
            session.Disconnect();
            return;
        }

        if (!identity.IdentityStable)
        {
            session.Context.Logger.Warning(
                $"Login '{identity.DisplayName}': no stable identity (identity/xid/chain) — " +
                $"using ephemeral uuid {identity.Uuid:D}; inventory/playerdata will not persist across rejoins.");
        }

        identity = ApplyJoinSkin(session, packet, identity);
        session.Profile = ClientProfileParser.Parse(
            packet.AuthInfo.Token,
            packet.AuthInfo.Certificate,
            packet.ClientDataJwt);

        var player = CreatePlayer(session, identity, TrustOf(authType, identity));
        session.Context.Logger.Info(
            $"Identity for '{player.Username}': uuid={player.Uuid:D} trust={player.Trust} " +
            "(list the uuid under permissions.operators to make an authenticated player an administrator).");
        HydratePlayer(session, player);

        if (!TryAdmit(session, player))
        {
            session.Disconnect();
            return;
        }

        session.Player = player;
        session.HasGameIdentity = true;

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
    /// Whether this server accepts the kind of login the client presents (auth.accept), and whether a NetherNet transport's
    /// authenticated identity agrees with it. Logs why when it does not.
    /// </summary>
    private static bool IsAuthenticationAllowed(NetworkSession session, LoginPacket packet, int authType)
    {
        var auth = session.Context.Config.Auth;
        var logger = session.Context.Logger;
        if (authType is < LoginPacket.AuthenticationInfo.TypeFull or > LoginPacket.AuthenticationInfo.TypeSelfSigned)
        {
            logger.Warning("Rejected login: unknown authentication type.");
            return false;
        }

        if (authType == LoginPacket.AuthenticationInfo.TypeGuest)
        {
            logger.Warning("Rejected login: guest authentication is not supported.");
            return false;
        }

        if (authType == LoginPacket.AuthenticationInfo.TypeFull && !auth.AllowsXbox)
        {
            logger.Warning("Rejected login: AuthenticationType FULL but auth.accept does not include xbox.");
            return false;
        }

        if (authType == LoginPacket.AuthenticationInfo.TypeSelfSigned && !auth.AllowsSelfSigned)
        {
            logger.Warning(
                "Rejected login: AuthenticationType SELF_SIGNED but auth.accept does not include self-signed.");
            return false;
        }

        if (session.RequiresTransportIdentityBinding &&
            !MatchesTransportAuthentication(session.TransportIdentity, authType, packet.AuthInfo.Token))
        {
            logger.Warning("Rejected NetherNet login: authenticated SDP and game authentication do not match.");
            return false;
        }

        return true;
    }

    /// <summary>Verifies the credentials (a FULL login's issuer signature and key proof) and reads who the player is.</summary>
    private static bool TryResolveIdentity(
        NetworkSession session, LoginPacket packet, int authType, out LoginIdentity.ParsedIdentity identity)
    {
        try
        {
            if (authType == LoginPacket.AuthenticationInfo.TypeFull)
            {
                if (!FranchiseTokenValidator.ValidateCachedLogin(packet.AuthInfo.Token, packet.ClientDataJwt))
                    throw new FormatException("FULL login issuer signature or client key proof was rejected.");
                // A legacy certificate is not the source of modern FULL identity.
                packet.AuthInfo.Certificate = null;
            }
            else if (!string.IsNullOrWhiteSpace(packet.AuthInfo.Certificate))
                LoginIdentity.ValidateIdentityChain(packet.AuthInfo.Certificate, requireStrictXbox: false);

            identity = LoginIdentity.ParseIdentityToken(
                packet.AuthInfo.Token, packet.AuthInfo.Certificate, packet.ClientDataJwt,
                authType == LoginPacket.AuthenticationInfo.TypeFull
                    ? new ServerConfig.AuthSection { Accept = [ServerConfig.AuthSection.ModeXbox] }
                    : session.Context.Config.Auth);
            return true;
        }
        catch (Exception ex)
        {
            session.Context.Logger.Warning($"Failed to parse/validate identity: {ex.Message}");
            identity = default;
            return false;
        }
    }

    /// <summary>
    /// Join skin = ClientData full SerializedSkin (DF parseSkin / PM ClientDataToSkinDataHelper).
    /// Mid-game PlayerSkin is secondary; nobody needs to change skin for peers to see it.
    /// </summary>
    private static LoginIdentity.ParsedIdentity ApplyJoinSkin(
        NetworkSession session, LoginPacket packet, LoginIdentity.ParsedIdentity identity)
    {
        if (!ClientSkinParser.TryParse(packet.ClientDataJwt, out var joinSkin))
            return LoginIdentity.AttachClientSkin(identity, packet.ClientDataJwt);

        session.Skin = joinSkin;
        session.SkinTrusted = ClientSkinParser.IsTrusted(packet.ClientDataJwt);
        return joinSkin.TryGetClassicRgba(out var rgba, out var sw, out var sh)
            ? identity with { SkinRgba = rgba, SkinWidth = sw, SkinHeight = sh }
            : identity;
    }

    /// <summary>Only a FULL login the server verified is authenticated; a stable id a client picked itself is merely asserted.</summary>
    private static Zenith.Gameplay.Permissions.IdentityTrust TrustOf(int authType, LoginIdentity.ParsedIdentity identity) =>
        !identity.IdentityStable ? Zenith.Gameplay.Permissions.IdentityTrust.Ephemeral
        : authType == LoginPacket.AuthenticationInfo.TypeFull ? Zenith.Gameplay.Permissions.IdentityTrust.Authenticated
        : Zenith.Gameplay.Permissions.IdentityTrust.SelfAsserted;

    private static Zenith.Player.Player CreatePlayer(
        NetworkSession session, LoginIdentity.ParsedIdentity identity, Zenith.Gameplay.Permissions.IdentityTrust trust)
    {
        var gameMode = Zenith.Player.GameModeConfig.FromConfig(session.Context.Config.Server.Gamemode);
        return new Zenith.Player.Player(
            identity.DisplayName,
            session,
            session.Context.PlayerManager.AllocateRuntimeId(),
            identity.Uuid,
            gameMode,
            identity.IdentityStable,
            session.Context.World,
            session.Context.ItemPalette,
            trust)
        {
            SkinRgba = identity.SkinRgba,
            SkinWidth = identity.SkinWidth,
            SkinHeight = identity.SkinHeight
        };
    }

    /// <summary>
    /// Restores what the player saved (bag, armor, offhand, pose, mode, experience, vitals) or places a first-time player on the
    /// terrain at the origin. Deliberately NOT <see cref="PlayerManager.TryAdd"/> yet — that is what makes a player visible to the
    /// GameLoop's Online snapshot, so everything is populated on this still-private object first and a concurrent tick can never
    /// observe a half-initialized player.
    /// </summary>
    private static void HydratePlayer(NetworkSession session, Zenith.Player.Player player)
    {
        var world = session.Context.World;
        var logger = session.Context.Logger;
        var loaded = world.Persistence.TryLoadInventory(player);
        logger.Info(
            loaded
                ? $"Inventory load hit for '{player.Username}' uuid={player.Uuid:D}"
                : $"Inventory load miss for '{player.Username}' uuid={player.Uuid:D} (starter/empty bag)");
        world.Persistence.TryLoadArmor(player.Uuid, player.Inventory);
        world.Persistence.TryLoadOffhand(player.Uuid, player.Inventory);

        if (!world.Persistence.TryLoadPlayerData(
                player.Uuid, out var px, out var py, out var pz, out var yaw, out var pitch, out var savedMode,
                out var savedLevel, out var savedPoints,
                out var savedHealth, out var savedHunger, out var savedSaturation, out var savedExhaustion))
        {
            // First join: stand on base terrain at world origin (noise-aware; ADR §63).
            player.PositionX = 0f;
            player.PositionY = world.SampleSpawnFeetY(0, 0);
            player.PositionZ = 0f;
            logger.Info(
                $"Playerdata load miss for '{player.Username}' uuid={player.Uuid:D} " +
                $"(terrain spawn Y={player.PositionY:F0} / config mode)");
            return;
        }

        player.PositionX = px;
        player.PositionY = py;
        player.PositionZ = pz;
        player.Yaw = yaw;
        player.Pitch = pitch;
        player.HeadYaw = yaw;
        player.SetGameMode(savedMode);
        player.SetExperience(savedLevel, savedPoints);
        player.HydrateVitals(savedHealth, savedHunger, savedSaturation, savedExhaustion);
        logger.Info(
            $"Playerdata load hit for '{player.Username}' @ {px:F1},{py:F1},{pz:F1} mode={savedMode} xp={savedLevel}/{savedPoints} " +
            $"hp={savedHealth:F1} hunger={savedHunger:F1}");

        // Flat-era pd: (Y≈-60) into noise hills → buried solid; client never leaves loading (PM/DF stand on surface).
        if (world.TryHealSpawnFeet(player.PositionX, player.PositionY, player.PositionZ, out var healedY))
        {
            player.PositionY = healedY;
            logger.Info($"Spawn heal for '{player.Username}': Y {py:F1} → {player.PositionY:F0} (clear feet at XZ)");
            world.Persistence.PersistPlayerData(player);
        }
    }

    /// <summary>Makes the finished player visible to the GameLoop, displacing a stale session of the same name once.</summary>
    private static bool TryAdmit(NetworkSession session, Zenith.Player.Player player)
    {
        var players = session.Context.PlayerManager;
        if (players.TryAdd(player)) return true;

        var existing = players.Get(player.Username);
        if (existing is not null)
        {
            session.Context.Logger.Warning($"Displacing already-online player '{player.Username}' for reconnect.");
            existing.Session.Disconnect();
        }

        if (players.TryAdd(player)) return true;

        session.Context.Logger.Warning($"Rejected login: '{player.Username}' already online.");
        return false;
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

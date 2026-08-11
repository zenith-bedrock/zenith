using System.Collections.Generic;
using Zenith.Gameplay;
using Zenith.Raknet.Stream;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>
/// Handler in-game. AuthInput/chat/blocos/ISR só registram intenção ou transmitem same-session —
/// posição/blocos/inventário finais no GameLoop.
/// </summary>
partial class InGameSessionHandler : ISessionHandler
{
    /// <summary>Decodes a packet that may legitimately be malformed on the wire (client bug,
    /// hostile input) - logs and returns false instead of letting the decode exception escape
    /// into the session's dispatch loop. Previously reimplemented per-handler with drifting
    /// exception-type/log-level choices (CommandRequest caught Exception+Warning, PlayerSkin
    /// caught only InvalidOperationException+Debug, Emote caught Exception+Debug).</summary>
    private static bool TryDecode<T>(NetworkSession session, ref BinaryStream stream, string label, out T packet, bool warnOnFailure = false)
        where T : DataPacket, new()
    {
        try
        {
            packet = DataPacket.From<T>(ref stream);
            return true;
        }
        catch (Exception ex)
        {
            var message = $"{label} decode rejected: {ex.Message}";
            if (warnOnFailure) session.Context.Logger.Warning(message);
            else session.Context.Logger.Debug(message);
            packet = new T();
            return false;
        }
    }

    public void OnEnable(NetworkSession session)
    {
        if (session.Player is not null)
        {
            session.Player.IsSpawning = false;
            session.Player.IsInGame = true;
            // Catch-up overlays placed while we were PreSpawn/SpawnResponse (§14).
            session.Player.Chunks.NeedsOverlayResync = true;
        }
        session.Context.Logger.Info($"{session.Player?.Username} is now in-game.");
    }

    public void OnDisable(NetworkSession session)
    {
        if (session.Player is not null)
        {
            session.Player.IsInGame = false;
            session.Player.IsSpawning = false;
        }
    }

    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        switch (header.Id)
        {
            case (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET:
                HandleAuthInput(session, ref stream);
                return true;

            case (int)ProtocolInfo.TEXT_PACKET:
                HandleText(session, ref stream);
                return true;

            case (int)ProtocolInfo.COMMAND_REQUEST_PACKET:
                HandleCommandRequest(session, ref stream);
                return true;

            case (int)ProtocolInfo.PLAYER_ACTION_PACKET:
                HandlePlayerAction(session, ref stream);
                return true;

            case (int)ProtocolInfo.RESPAWN_PACKET:
                HandleRespawn(session, ref stream);
                return true;

            case (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET:
                HandleInventoryTransaction(session, ref stream);
                return true;

            case (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET:
                HandleItemStackRequest(session, ref stream);
                return true;

            case (int)ProtocolInfo.REQUEST_CHUNK_RADIUS_PACKET:
                HandleRequestChunkRadius(session, ref stream);
                return true;

            case (int)ProtocolInfo.MOVE_PLAYER_PACKET:
                return true;

            case (int)ProtocolInfo.MOB_EQUIPMENT_PACKET:
                HandleMobEquipment(session, ref stream);
                return true;

            case (int)ProtocolInfo.INTERACT_PACKET:
                HandleInteract(session, ref stream);
                return true;

            case (int)ProtocolInfo.CONTAINER_CLOSE_PACKET:
                HandleContainerClose(session, ref stream);
                return true;

            case (int)ProtocolInfo.REQUEST_ABILITY_PACKET:
                HandleRequestAbility(session, ref stream);
                return true;

            case (int)ProtocolInfo.DISCONNECT_PACKET:
                HandleDisconnect(session, ref stream);
                return true;

            case (int)ProtocolInfo.PLAYER_SKIN_PACKET:
                HandlePlayerSkin(session, ref stream);
                return true;

            case (int)ProtocolInfo.ANIMATE_PACKET:
            case (int)ProtocolInfo.LEVEL_SOUND_EVENT_PACKET:
            case (int)ProtocolInfo.EMOTE_LIST_PACKET:
            case (int)ProtocolInfo.MODAL_FORM_RESPONSE_PACKET:
            case (int)ProtocolInfo.SERVER_SETTINGS_REQUEST_PACKET:
            case (int)ProtocolInfo.SERVERBOUND_LOADING_SCREEN_PACKET:
            case (int)ProtocolInfo.SET_PLAYER_INVENTORY_OPTIONS_PACKET:
                return true;

            case (int)ProtocolInfo.EMOTE_PACKET:
                HandleEmote(session, ref stream);
                return true;

            default:
                return false;
        }
    }

    private static void HandleRequestChunkRadius(NetworkSession session, ref BinaryStream stream)
    {
        var request = DataPacket.From<RequestChunkRadiusPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        var cap = session.Context.Config.World.SpawnChunkRadius;
        var radius = Math.Min(request.Radius, cap);
        player.Chunks.Radius = radius;
        session.Protocol.World.SendChunkRadiusUpdated(radius);
        session.Context.Logger.Debug($"In-game chunk radius updated for {player.Username}: {radius}");
    }

    private static void HandleText(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<TextPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        if (packet.Type != TextPacket.TypeChat) return;

        // Slash lines never fan-out via ChatSystem (§52).
        if (packet.Message.StartsWith('/'))
        {
            HandleCommandLine(session, player, packet.Message);
            return;
        }

        if (!session.Protocol.Chat.TryAcceptOutboundChat(player.Uuid, packet.Message, out var message))
        {
            session.Context.Logger.Debug($"Chat rejected from {player.Username} (rate/size/empty).");
            return;
        }

        if (!player.SubmitChat(message))
            session.Context.Logger.Debug($"Dropped chat from {player.Username}: chat queue full.");
    }

    private static void HandleCommandRequest(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<CommandRequestPacket>(session, ref stream, "CommandRequest", out var packet, warnOnFailure: true))
            return;

        var player = session.Player;
        if (player is null) return;

        session.Context.Logger.Info($"{player.Username} CommandRequest: {packet.CommandLine}");

        HandleCommandLine(session, player, packet.CommandLine);
    }

    private static void HandleCommandLine(NetworkSession session, Player.Player player, string line)
    {
        var feedback = session.Context.Commands.Execute(player, line);
        session.Protocol.Ui.SendToast(feedback.Title, feedback.Message);
    }

    private static void HandleRequestAbility(NetworkSession session, ref BinaryStream stream)
    {
        // ADR §97 immediate runtime operation: this packet only requests a same-session wire
        // refresh. It neither mutates Player ability authority nor affects gameplay ordering.
        var packet = new RequestAbilityPacket();
        packet.Decode(ref stream);

        if (packet.Ability != AbilityBits.Flying)
            return;

        var player = session.Player;
        if (player is null) return;

        if (player.GameMode != GameMode.Creative)
            return;

        session.Protocol.Entity.SendLocalAbilities(
            player.RuntimeId,
            AbilityBits.WireGameModeCreative,
            flying: packet.BoolValue);
    }

    private static void HandleDisconnect(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<DisconnectPacket>(ref stream);
        session.Context.Logger.Info(
            $"DisconnectPacket from {session.Player?.Username ?? session.RakSession.EndPoint.ToString()}: " +
            $"reason={packet.Reason}, message={packet.Message}");
        session.Disconnect();
    }

    private static void HandlePlayerSkin(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<PlayerSkinPacket>(session, ref stream, "PlayerSkin", out var packet))
            return;

        var player = session.Player;
        if (player is null) return;

        if (!Guid.TryParse(packet.Uuid, out var packetUuid) || packetUuid != player.Uuid)
        {
            session.Context.Logger.Debug(
                $"PlayerSkin ignored for {player.Username}: uuid mismatch (packet={packet.Uuid}).");
            return;
        }

        if (packet.Skin.TryGetClassicRgba(out var rgba, out var width, out var height))
        {
            player.SkinRgba = rgba;
            player.SkinWidth = width;
            player.SkinHeight = height;
        }

        PlayerVisibility.RelaySkin(
            player,
            packet.Skin,
            packet.SkinName,
            packet.OldSkinName,
            packet.IsVerified,
            session.Context.PlayerManager.SnapshotOnline());
    }

    private static void HandleEmote(NetworkSession session, ref BinaryStream stream)
    {
        if (!TryDecode<EmotePacket>(session, ref stream, "Emote", out var packet))
            return;

        var player = session.Player;
        if (player is null || player.IsDead) return;

        if ((long)packet.ActorRuntimeId != player.RuntimeId)
        {
            session.Context.Logger.Debug(
                $"Emote ignored for {player.Username}: runtime id mismatch.");
            return;
        }

        if (string.IsNullOrEmpty(packet.EmoteId))
            return;

        var tick = session.Context.Clock.CurrentTick;
        if (player.LastEmoteTick != 0 && tick - player.LastEmoteTick < 20)
            return;

        player.LastEmoteTick = tick;
        var xuid = !string.IsNullOrEmpty(player.Session.Profile.Xuid)
            ? player.Session.Profile.Xuid
            : packet.Xuid;
        var platformChatId = !string.IsNullOrEmpty(player.Session.Profile.PlatformChatId)
            ? player.Session.Profile.PlatformChatId
            : packet.PlatformChatId;
        PlayerVisibility.RelayEmote(
            player,
            packet.EmoteId,
            packet.TickLength,
            xuid,
            platformChatId,
            session.Context.PlayerManager.SnapshotOnline());
    }

}

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
    public void OnEnable(NetworkSession session)
    {
        if (session.Player is not null)
        {
            session.Player.IsInGame = true;
            // Catch-up overlays placed while we were PreSpawn/SpawnResponse (§14).
            session.Player.Chunks.NeedsOverlayResync = true;
        }
        session.Context.Logger.Info($"{session.Player?.Username} is now in-game.");
    }

    public void OnDisable(NetworkSession session)
    {
        if (session.Player is not null)
            session.Player.IsInGame = false;
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
            TryHandleGamemodeLine(session, player, packet.Message);
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
        var packet = DataPacket.From<CommandRequestPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        // Bedrock slash channel (§52 adendo). Unknown commands: quiet ignore.
        TryHandleGamemodeLine(session, player, packet.CommandLine);
    }

    private static void TryHandleGamemodeLine(NetworkSession session, Player.Player player, string line)
    {
        if (GameModeConfig.TryParseCommand(line, out var mode, out var badArgs))
        {
            player.SubmitGameMode(mode);
            return;
        }

        if (badArgs)
        {
            session.Protocol.Ui.SendToast(
                "Game mode",
                "Usage: /gamemode survival|creative");
        }
    }

    private static void HandleRequestAbility(NetworkSession session, ref BinaryStream stream)
    {
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
        PlayerSkinPacket packet;
        try
        {
            packet = DataPacket.From<PlayerSkinPacket>(ref stream);
        }
        catch (InvalidOperationException ex)
        {
            session.Context.Logger.Debug($"PlayerSkin decode rejected: {ex.Message}");
            return;
        }

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
        EmotePacket packet;
        try
        {
            packet = DataPacket.From<EmotePacket>(ref stream);
        }
        catch (Exception ex)
        {
            session.Context.Logger.Debug($"Emote decode rejected: {ex.Message}");
            return;
        }

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
        PlayerVisibility.RelayEmote(
            player,
            packet.EmoteId,
            packet.TickLength,
            packet.Xuid,
            packet.PlatformChatId,
            session.Context.PlayerManager.SnapshotOnline());
    }

}

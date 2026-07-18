using Zenith.Packets;
using Zenith.Player;

namespace Zenith.Session;

/// <summary>
/// Orquestra a ordem do fan-out join/leave. Não é GameSystem / VisibilitySystem —
/// só sequencia Protocol a partir do estado atual do <see cref="Player"/>.
/// </summary>
static class PlayerVisibility
{
    public static void AnnounceJoin(Player.Player joiner, IReadOnlyCollection<Player.Player> online)
    {
        var peers = online
            .Where(p => p.IsInGame && !ReferenceEquals(p, joiner))
            .ToArray();

        // 1. Joiner vê peers já in-game
        foreach (var peer in peers)
        {
            SendPlayerListAdd(joiner, peer);
            SendAddPlayer(joiner, peer);
        }

        // 2. Todos (incl. joiner) recebem PlayerList(ADD joiner)
        SendPlayerListAdd(joiner, joiner);
        foreach (var peer in peers)
            SendPlayerListAdd(peer, joiner);

        // 3. Cada peer recebe AddPlayer(joiner) — não enviar AddPlayer de self pra self
        foreach (var peer in peers)
            SendAddPlayer(peer, joiner);
    }

    public static void AnnounceLeave(Player.Player leaving, IReadOnlyCollection<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, leaving)) continue;
            peer.Session.Protocol.Entity.SendPlayerListRemove(leaving.Uuid);
            peer.Session.Protocol.Entity.SendRemoveActor(leaving.RuntimeId);
        }
    }

    /// <summary>
    /// Mid-game skin change: relay <see cref="PlayerSkinPacket"/> to other InGame peers only.
    /// Join/late-join still uses SkinRgba + SkinWire on PlayerList (persona Deferred).
    /// </summary>
    public static void RelaySkin(
        Player.Player subject,
        SerializedSkin skin,
        string skinName,
        string oldSkinName,
        bool isVerified,
        IReadOnlyCollection<Player.Player> online)
    {
        var uuid = subject.Uuid.ToString("D");
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Session.Protocol.Skin.SendSkin(uuid, skin, skinName, oldSkinName, isVerified);
        }
    }

    /// <summary>
    /// Emote relay to other InGame peers (§53). Caller validates runtime id + rate-limit.
    /// </summary>
    public static void RelayEmote(
        Player.Player subject,
        string emoteId,
        uint tickLength,
        string xuid,
        string platformChatId,
        IReadOnlyCollection<Player.Player> online)
    {
        const byte flags = (byte)(EmotePacket.FlagServerSide | EmotePacket.FlagMuteChat);
        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Session.Protocol.Entity.SendEmote(rid, emoteId, tickLength, xuid, platformChatId, flags);
        }
    }

    /// <summary>
    /// Arm swing to other InGame peers (§53) — dig / place / attack / MissedSwing.
    /// </summary>
    public static void RelaySwingArm(
        Player.Player subject,
        IReadOnlyCollection<Player.Player> online,
        string? swingSource = "attack")
    {
        if (subject.IsDead) return;
        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Session.Protocol.Entity.SendAnimateSwingArm(rid, swingSource);
        }
    }

    private static void SendPlayerListAdd(Player.Player recipient, Player.Player subject)
    {
        recipient.Session.Protocol.Entity.SendPlayerListAdd(
            subject.Uuid,
            subject.RuntimeId,
            subject.Username,
            subject.SkinRgba,
            subject.SkinWidth,
            subject.SkinHeight);
    }

    private static void SendAddPlayer(Player.Player recipient, Player.Player subject)
    {
        var held = subject.Session.Protocol.Inventory.DescribeSlot(
            subject.Inventory,
            subject.SelectedHotbarSlot);
        recipient.Session.Protocol.Entity.SendAddPlayer(
            subject.Uuid,
            subject.Username,
            (ulong)subject.RuntimeId,
            subject.PositionX,
            subject.PositionY,
            subject.PositionZ,
            subject.Pitch,
            subject.Yaw,
            subject.HeadYaw,
            held,
            gameMode: (int)subject.GameMode,
            sneaking: subject.IsSneaking,
            sprinting: subject.IsSprinting);
        // ADR §44: dirty-check suppresses Absolute while pose is unchanged. New viewers only
        // get AddPlayer (feet) until the subject moves. Follow with Absolute (feet→wire +1.621)
        // so peers settle on the ground — bare feet Absolute sinks the model (~eye height).
        recipient.Session.Protocol.Entity.SendMoveAbsolute(
            (ulong)subject.RuntimeId,
            subject.PositionX,
            subject.PositionY,
            subject.PositionZ,
            subject.Pitch,
            subject.Yaw,
            subject.HeadYaw,
            flags: MoveActorAbsolutePacket.FLAG_ON_GROUND);
    }
}

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

        // 2. Peers recebem PlayerList(ADD joiner). The joining client has no AddPlayer(self),
        // so it does not require a self entry to construct its local actor. Keeping this out of
        // the initial bootstrap also avoids replaying its full ClientData skin to itself.
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
    /// Mid-game skin change: update subject's Session skin + relay PlayerSkin to peers.
    /// Join/late-join uses the same Session.Skin on PlayerList (full SerializedSkin).
    /// </summary>
    public static void RelaySkin(
        Player.Player subject,
        SerializedSkin skin,
        string skinName,
        string oldSkinName,
        bool isVerified,
        IReadOnlyCollection<Player.Player> online)
    {
        subject.Session.Skin = skin;
        subject.Session.SkinTrusted = isVerified;
        var uuid = subject.Uuid.ToString("D");
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Session.Protocol.Skin.SendSkin(uuid, skin, skinName, oldSkinName, isVerified);
        }
    }

    /// <summary>
    /// Mid-game GameMode change: peers must re-see subject with new GameMode on AddPlayer (§59).
    /// PlayerList is left alone (UUID already known).
    /// </summary>
    public static void RefreshPeerView(Player.Player subject, IReadOnlyCollection<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Session.Protocol.Entity.SendRemoveActor(subject.RuntimeId);
            SendAddPlayer(peer, subject);
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

    /// <summary>
    /// Replicates a player's already-decided health state to existing peers. The subject's own
    /// HUD uses the complete local attribute seed; a late viewer receives the same health in
    /// <see cref="SendAddPlayer"/>.
    /// </summary>
    public static void RelayHealth(
        Player.Player subject,
        IReadOnlyList<Player.Player> online)
    {
        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Session.Protocol.Entity.SendHealth(rid, subject.Health, subject.MaxHealth);
        }
    }

    private static void SendPlayerListAdd(Player.Player recipient, Player.Player subject)
    {
        var profile = subject.Session.Profile;
        recipient.Session.Protocol.Entity.SendPlayerListAdd(
            subject.Uuid,
            subject.RuntimeId,
            subject.Username,
            subject.Session.Skin,
            subject.SkinRgba,
            subject.SkinWidth,
            subject.SkinHeight,
            subject.Session.SkinTrusted,
            profile.Xuid,
            profile.PlatformChatId,
            profile.BuildPlatform);
    }

    private static void SendAddPlayer(Player.Player recipient, Player.Player subject)
    {
        var profile = subject.Session.Profile;
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
            sprinting: subject.IsSprinting,
            platformChatId: profile.PlatformChatId,
            deviceId: profile.DeviceId,
            buildPlatform: profile.BuildPlatform);

        // PlayerList immediately preceding AddPlayer already carries the complete SerializedSkin.
        // Do not replay it as PlayerSkin while the recipient constructs the peer actor: PlayerSkin
        // is for a later skin change. Empty armour is implicit, but an already-equipped peer
        // needs one initial equipment projection before later armor deltas can apply.
        var subjectInventory = subject.Inventory;
        var head = subjectInventory.GetArmor(PlayerInventory.ArmorHelmetSlot);
        var torso = subjectInventory.GetArmor(PlayerInventory.ArmorChestplateSlot);
        var legs = subjectInventory.GetArmor(PlayerInventory.ArmorLeggingsSlot);
        var feet = subjectInventory.GetArmor(PlayerInventory.ArmorBootsSlot);
        if (!head.IsEmpty || !torso.IsEmpty || !legs.IsEmpty || !feet.IsEmpty)
        {
            var inventoryProtocol = subject.Session.Protocol.Inventory;
            recipient.Session.Protocol.Entity.SendMobArmorEquipment(
                (ulong)subject.RuntimeId,
                inventoryProtocol.DescribeArmor(subjectInventory, PlayerInventory.ArmorHelmetSlot),
                inventoryProtocol.DescribeArmor(subjectInventory, PlayerInventory.ArmorChestplateSlot),
                inventoryProtocol.DescribeArmor(subjectInventory, PlayerInventory.ArmorLeggingsSlot),
                inventoryProtocol.DescribeArmor(subjectInventory, PlayerInventory.ArmorBootsSlot));
        }
    }
}

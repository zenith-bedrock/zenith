using Zenith.Packets;
using Zenith.Player;

namespace Zenith.Session;

/// <summary>
/// Relays that need the session itself (the subject's skin and client profile, wire emote flags), so they live with the session
/// layer and not with <see cref="Zenith.Gameplay.Replication.PlayerVisibility"/>.
/// </summary>
static class PeerRelay
{
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
}

using Zenith.Player;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Orquestra a ordem do fan-out join/leave e os relays de estado do jogador para os peers. Não é GameSystem / VisibilitySystem —
/// só sequencia o que cada peer recebe, pelo <see cref="Ports.IClientChannel"/> de cada um.
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
            joiner.Client.Entity.SendPlayerListEntryOf(peer);
            joiner.Client.Entity.SendPlayerActorOf(peer);
        }

        // 2. Peers recebem PlayerList(ADD joiner). The joining client has no AddPlayer(self),
        // so it does not require a self entry to construct its local actor. Keeping this out of
        // the initial bootstrap also avoids replaying its full ClientData skin to itself.
        foreach (var peer in peers)
            peer.Client.Entity.SendPlayerListEntryOf(joiner);

        // 3. Cada peer recebe AddPlayer(joiner) — não enviar AddPlayer de self pra self
        foreach (var peer in peers)
            peer.Client.Entity.SendPlayerActorOf(joiner);
    }

    public static void AnnounceLeave(Player.Player leaving, IReadOnlyCollection<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, leaving)) continue;
            peer.Client.Entity.SendPlayerListRemove(leaving.Uuid);
            peer.Client.Entity.SendRemoveActor(leaving.RuntimeId);
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
            peer.Client.Entity.SendRemoveActor(subject.RuntimeId);
            peer.Client.Entity.SendPlayerActorOf(subject);
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
            peer.Client.Entity.SendAnimateSwingArm(rid, swingSource);
        }
    }

    /// <summary>
    /// Replicates a player's already-decided health state to existing peers. The subject's own
    /// HUD uses the complete local attribute seed; a late viewer receives the same health in
    /// <see cref="Ports.IEntityChannel.SendPlayerActorOf"/>.
    /// </summary>
    public static void RelayHealth(
        Player.Player subject,
        IReadOnlyList<Player.Player> online)
    {
        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Client.Entity.SendHealth(rid, subject.Health, subject.MaxHealth);
        }
    }

    /// <summary>
    /// Non-lethal hurt reaction to other peers (Phase XXIII) — the subject's own client already
    /// infers its hurt state from the incoming health change, so this is peer-only like
    /// <see cref="RelaySwingArm"/>.
    /// </summary>
    public static void RelayHurt(
        Player.Player subject,
        IReadOnlyList<Player.Player> online)
    {
        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Client.Entity.SendHurt(rid);
        }
    }

    /// <summary>Death reaction to other peers (Phase XXIII) — the subject gets its own death flow via <see cref="Ports.IEntityChannel.SendDeathInfo"/>.</summary>
    public static void RelayDeath(
        Player.Player subject,
        IReadOnlyList<Player.Player> online)
    {
        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Client.Entity.SendDeath(rid);
        }
    }
}

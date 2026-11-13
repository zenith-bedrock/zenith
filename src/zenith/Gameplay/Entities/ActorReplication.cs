using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Ports;
using Zenith.Protocol;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Everything an ECS-stored actor species needs to be shown to players: which peers currently see which actor
/// (<see cref="Viewers"/>), the last pose each peer was sent (so a move is only sent when it changed), spawn, removal on
/// despawn and the batched move packet. Zombie, Cow, Creeper, Spider, Skeleton, Golem and Minecart each carried a
/// byte-identical copy of this (they differed only in which <c>SendAddX</c> packet announces the actor); a species now
/// owns one of these and supplies just that packet.
///
/// <paramref name="trackPoses"/> is false for a species whose position is never replicated by move packets (an enderman only
/// teleports), so it keeps no pose cache.
///
/// It owns replication bookkeeping and the despawn lifecycle, nothing about what a species does: no AI, damage or loot.
/// </summary>
sealed class ActorReplication(EntityRuntime stores, MobKind kind, bool trackPoses = true)
{
    private readonly Dictionary<(long EntityId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private readonly HashSet<long> _aliveScratch = [];

    /// <summary>(actor unique id, peer runtime id) pairs for every actor a peer has been told about; shared with combat so a death is announced to exactly those peers.</summary>
    public HashSet<(long EntityId, long PlayerId)> Viewers { get; } = [];

    public long SpawnCount;
    public long MoveCount;
    public long MoveSkippedCount;
    public long RemovalCount;
    public long DespawnCount;

    /// <summary>
    /// Brings one actor's viewer set up to date: peers that became interested get <paramref name="sendSpawn"/> then the
    /// current health (then <paramref name="afterSpawn"/>, for species with more to tell a newcomer), peers that lost
    /// interest get the removal.
    /// </summary>
    public void Reconcile(
        EntityId id,
        IReadOnlyList<Player.Player> online,
        Action<Player.Player, ActorIdentity>? afterSpawn = null)
    {
        if (!stores.Positions.TryGet(id, out var pos)) return;
        if (!stores.Identities.TryGet(id, out var identity)) return;
        if (!stores.Health.TryGet(id, out var healthComponent)) return;
        var health = healthComponent.State;

        ViewerReconciliation.Sync(
            identity.ActorUniqueId, online, Viewers,
            peer => ActorInterest.Includes(peer, pos.X, pos.Z),
            onEnter: peer =>
            {
                peer.Client.Entity.SendAddMob(kind, identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
                peer.Client.Entity.SendHealth(identity.ActorRuntimeId, health.Current, health.Maximum);
                afterSpawn?.Invoke(peer, identity);
                if (trackPoses) _lastProjected[(identity.ActorUniqueId, peer.RuntimeId)] = new ProjectedPose(pos.X, pos.Y, pos.Z, pos.Yaw);
                SpawnCount++;
            },
            onExit: peer =>
            {
                _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                peer.Client.Entity.SendRemoveActor(identity.ActorUniqueId);
                RemovalCount++;
            });
    }

    /// <summary>Drops the pose cache for one peer after the actor's death was announced to it by combat.</summary>
    public void Forget(long actorUniqueId, Player.Player peer) =>
        _lastProjected.Remove((actorUniqueId, peer.RuntimeId));

    /// <summary>
    /// Lifecycle removal if no player has been near for long enough (<see cref="DespawnLifecycle"/>): tells every peer who saw
    /// the actor and destroys it. Not a death: no loot, no experience. <paramref name="beforeDestroy"/> runs once the
    /// decision is made, for a species that must release something first (a minecart's rider).
    /// </summary>
    public bool TryDespawn(
        EntityId id, GameClock clock, IReadOnlyList<Player.Player> online, float radius, Action<EntityId>? beforeDestroy = null)
    {
        if (!stores.Positions.TryGet(id, out var pos)) return false;
        ref var tracking = ref stores.Despawn.GetRef(id);
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            pos.X, pos.Z, online, radius, clock.CurrentTick, tracking.LastSeenNearPlayerTick);
        tracking.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        beforeDestroy?.Invoke(id);
        if (!stores.Identities.TryGet(id, out var identity)) return false;
        foreach (var peer in online)
        {
            if (!Viewers.Remove((identity.ActorUniqueId, peer.RuntimeId))) continue;
            _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
            peer.Client.Entity.SendRemoveActor(identity.ActorUniqueId);
            RemovalCount++;
        }

        stores.DestroyActor(identity.ActorRuntimeId, id);
        DespawnCount++;
        return true;
    }

    /// <summary>Forgets viewer pairs whose actor is gone or whose peer left. One pass over the live actors, not one per pair.</summary>
    public void Prune(IReadOnlyList<EntityId> alive, IReadOnlyList<Player.Player> online)
    {
        _aliveScratch.Clear();
        foreach (var id in alive)
            if (stores.Identities.TryGet(id, out var identity))
                _aliveScratch.Add(identity.ActorUniqueId);

        Viewers.RemoveWhere(pair => !_aliveScratch.Contains(pair.EntityId) || !IsOnline(online, pair.PlayerId));
        List<(long, long)>? stale = null;
        foreach (var key in _lastProjected.Keys)
            if (!Viewers.Contains(key)) (stale ??= []).Add(key);
        if (stale is not null)
            foreach (var key in stale) _lastProjected.Remove(key);
    }

    /// <summary>One move packet per peer with every actor of <paramref name="entities"/> that peer sees and that moved or turned.</summary>
    public void ReplicateMoves(IReadOnlyList<EntityId> entities, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var id in entities)
            {
                if (!stores.Identities.TryGet(id, out var identity)) continue;
                var key = (identity.ActorUniqueId, peer.RuntimeId);
                if (!Viewers.Contains(key)) continue;
                if (!stores.Positions.TryGet(id, out var pos)) continue;
                var current = new ProjectedPose(pos.X, pos.Y, pos.Z, pos.Yaw);
                if (_lastProjected.TryGetValue(key, out var previous) && !current.MeaningfullyChanged(previous))
                {
                    MoveSkippedCount++;
                    continue;
                }

                _moveBatch.Add(new RawActorPose
                {
                    ActorRuntimeId = identity.ActorRuntimeId,
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z,
                    Yaw = pos.Yaw,
                    HeadYaw = pos.Yaw,
                });
                _lastProjected[key] = current;
                MoveCount++;
            }

            peer.Client.Entity.SendMoveActorAbsoluteRaws(_moveBatch);
        }
    }

    private static bool IsOnline(IReadOnlyList<Player.Player> online, long runtimeId)
    {
        foreach (var player in online)
            if (player.RuntimeId == runtimeId) return true;
        return false;
    }
}

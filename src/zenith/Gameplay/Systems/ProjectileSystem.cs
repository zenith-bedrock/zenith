using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Phase XXI — third ECS-authoritative actor, deliberately chosen to prove the ECS is not built
/// around <see cref="IDamageableActor"/>: a projectile has Position/Velocity/ActorIdentity but no
/// <see cref="HealthComponent"/> at all (it deals damage, never takes it), and its lifetime
/// (<see cref="ProjectileState.AgeTicks"/>) is unconditional pure age, not sharing
/// <see cref="DespawnTracking"/> with the visibility-reset family.
///
/// Also where the old <c>ProjectileSystem → ZombieStore</c> coupling (audited per the phase brief,
/// item 21) was replaced with a real cross-species hit query: <see cref="FindHitDamageableActor"/>
/// walks <c>Query.With(stores.Health, stores.Positions)</c> — the driving store is Health,
/// deliberately, since only migrated damageable actors have one and projectiles don't, making it
/// the smaller set to iterate. Which system actually owns the hit entity and applies the damage is
/// resolved via <see cref="DamageDispatch"/> (Phase XXII) — a small registered-handler list, not a
/// hardcoded per-species chain and not a global spatial index (neither was warranted at these actor
/// counts — see docs/history/phases/phase-xxi-ecs-foundation-findings.md and
/// docs/history/phases/phase-xxii-ecs-roster-consolidation-findings.md).
/// </summary>
sealed class ProjectileSystem : IGameSystem
{
    private const float LaunchSpeed = 0.5f;
    private const float LaunchUpwardVelocity = 0.1f;

    /// <summary>
    /// Internal (not private) so ranged shooters — currently only <see cref="SkeletonSystem"/> — can
    /// compute a launch arc that actually reaches the target instead of guessing a fixed vertical
    /// velocity. Phase XXIII-B fix: Skeleton previously launched with a flat +0.08 vertical velocity
    /// regardless of range; against this same per-tick gravity, the accumulated drop over the many
    /// ticks a real shot distance takes (e.g. ~22 ticks at 10 blocks) is several blocks — the arrow
    /// was hitting the ground and despawning well short of the player, read by a real client as "the
    /// snowball disappears before hitting me".
    ///
    /// Value corrected in the same pass's cross-reference review: was 0.03, but PocketMine's
    /// <c>Arrow::getInitialGravity()</c> (D:\Development\bedrock\pocketmine\src\entity\projectile\
    /// Arrow.php) returns 0.05 — matching vanilla. Symbolic (not hand-tuned) throughout this file and
    /// in SkeletonSystem's ballistic-arc formula, so this single constant fix keeps both consistent.
    /// </summary>
    internal const float GravityPerTick = 0.05f;
    private const float HitRadius = 0.55f;
    private const float Damage = 6f;
    private const int MaximumLifetimeTicks = 80;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<ProjectileState> _projectiles;
    private readonly DamageDispatch _damage;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = [];
    private readonly Dictionary<(long EntityId, long PlayerId), ProjectedPosition> _lastProjected = [];
    private readonly List<RawActorPose> _moveBatch = [];
    private readonly List<EntityId> _tickScratch = []; // Reused per tick — see ZombieSystem's identical field for why.

    public ProjectileSystem(World.World world, PlayerManager players, EntityRuntime stores, DamageDispatch damage)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _projectiles = new ComponentStore<ProjectileState>(stores.Entities);
        _damage = damage;
    }

    internal IReadOnlyList<EntityId> Projectiles => _projectiles.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<ProjectileState> ProjectileStates => _projectiles;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedMoveSkippedCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long RemovalCount { get; private set; }

    /// <summary>Concrete gameplay spawn used by a ranged actor; this system remains the projectile lifecycle owner.</summary>
    public bool TrySpawnFromActor(long ownerRuntimeId, float x, float y, float z, float velocityX, float velocityY, float velocityZ,
        IReadOnlyList<Player.Player> online)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Projectile.");
        _stores.Velocities.Set(id, new Velocity { X = velocityX, Y = velocityY, Z = velocityZ });
        _projectiles.Set(id, new ProjectileState { OwnerRuntimeId = ownerRuntimeId, AgeTicks = 0 });
        ReconcileViewers(id, online);
        return true;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        SpawnFromPlayerInputs(online);

        _tickScratch.Clear();
        _tickScratch.AddRange(_projectiles.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            ReconcileViewers(id, online);
            Advance(id, online, clock.CurrentTick);
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveProjectileId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private bool IsKnownAliveProjectileId(long actorUniqueId)
    {
        foreach (var id in _projectiles.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
    }

    private void SpawnFromPlayerInputs(IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead || !player.TryConsumeProjectileIntent()) continue;
            var radians = player.Yaw * (MathF.PI / 180f);
            var velocityX = -MathF.Sin(radians) * LaunchSpeed;
            var velocityZ = MathF.Cos(radians) * LaunchSpeed;
            _ = TrySpawnFromActor(player.RuntimeId, player.PositionX, player.PositionY + 1.2f, player.PositionZ,
                velocityX, LaunchUpwardVelocity, velocityZ, online);
        }
    }

    private void Advance(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_stores.Velocities.TryGet(id, out var vel)) return;
        if (!_projectiles.TryGet(id, out var state)) return;

        var nextX = pos.X + vel.X;
        var nextY = pos.Y + vel.Y;
        var nextZ = pos.Z + vel.Z;

        var hitActor = FindHitDamageableActor(nextX, nextY, nextZ, state.OwnerRuntimeId);
        if (hitActor is { } actorId)
        {
            TryDamageActor(actorId, DamageSource.Projectile(state.OwnerRuntimeId), Damage, online, currentTick);
            Remove(id, online);
            return;
        }

        var player = FindHitPlayer(state.OwnerRuntimeId, nextX, nextY, nextZ, online);
        if (player is not null)
        {
            // Knockback direction follows the arrow's own flight, not the shooter's position —
            // matches vanilla (an arrow that curved under gravity still knocks the target the way
            // it was actually travelling at impact).
            PlayerDamage.Apply(player, _players, online, DamageSource.Projectile(state.OwnerRuntimeId), Damage, currentTick, vel.X, vel.Z);
            Remove(id, online);
            return;
        }

        ref var trackedState = ref _projectiles.GetRef(id);
        if (HitsWorld(nextX, nextY, nextZ) || ++trackedState.AgeTicks >= MaximumLifetimeTicks)
        {
            Remove(id, online);
            return;
        }

        ref var p = ref _stores.Positions.GetRef(id);
        p.X = nextX;
        p.Y = nextY;
        p.Z = nextZ;
        ref var v = ref _stores.Velocities.GetRef(id);
        v.Y -= GravityPerTick;
        ReconcileViewers(id, online);
    }

    /// <summary>
    /// Cross-species hit test: any ECS entity with both Health and Position is a candidate,
    /// regardless of which system spawned it — this is the generalization the ECS foundation was
    /// meant to make possible (see this class's doc comment). Health drives the query since it is
    /// the smaller set (projectiles themselves, and any future non-damageable actor, never match).
    ///
    /// Phase XXIII-B fix: this never excluded the shooter itself. A real client crash traced back
    /// to Skeleton — the one ECS-damageable species that also fires projectiles — hitting itself on
    /// the very first movement step (the arrow spawns almost exactly at the shooter's own position),
    /// killing it a tick after spawn. Player-fired arrows never showed this because Player isn't an
    /// ECS entity, so this query could never match the shooter in that case. <paramref
    /// name="ownerRuntimeId"/> excludes whichever ECS entity actually fired this projectile,
    /// regardless of species.
    /// </summary>
    private EntityId? FindHitDamageableActor(float x, float y, float z, long ownerRuntimeId)
    {
        foreach (var candidateId in Query.With(_stores.Health, _stores.Positions))
        {
            if (_stores.Identities.TryGet(candidateId, out var identity) && identity.ActorRuntimeId == (ulong)ownerRuntimeId)
                continue;
            if (!_stores.Positions.TryGet(candidateId, out var pos)) continue;
            if (MathF.Abs(pos.X - x) > HitRadius || MathF.Abs(pos.Z - z) > HitRadius) continue;
            if (y < pos.Y || y > pos.Y + 2f) continue;
            return candidateId;
        }
        return null;
    }

    /// <summary>
    /// Dispatches through <see cref="DamageDispatch"/> to whichever migrated system actually owns
    /// the hit entity. Deliberately not a generic "any damageable actor" damage call inside the ECS
    /// itself — loot/XP/removal specifics stay per-system, exactly the "share data, not behavior"
    /// principle applied to a cross-species query result.
    /// </summary>
    private bool TryDamageActor(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _damage.TryApplyDamage(id, source, amount, online, currentTick);

    private void ReplicateMoves(IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var id in _projectiles.Entities)
            {
                if (!_stores.Identities.TryGet(id, out var identity)) continue;
                var key = (identity.ActorUniqueId, peer.RuntimeId);
                if (!_replicated.Contains(key)) continue;
                if (!_stores.Positions.TryGet(id, out var pos)) continue;
                var current = new ProjectedPosition(pos.X, pos.Y, pos.Z);
                if (_lastProjected.TryGetValue(key, out var previous) && !current.MeaningfullyChanged(previous))
                {
                    ReplicatedMoveSkippedCount++;
                    continue;
                }
                _moveBatch.Add(new RawActorPose
                {
                    ActorRuntimeId = identity.ActorRuntimeId,
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z
                });
                _lastProjected[key] = current;
                ReplicatedMoveCount++;
            }
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaws(_moveBatch);
        }
    }

    private static Player.Player? FindHitPlayer(long ownerRuntimeId, float x, float y, float z,
        IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            // The existing player snowball slice only targets Zombies/Minecarts. Concrete ranged
            // actors use an id not owned by an online player, which lets their projectile affect
            // players without silently introducing player-vs-player combat in this phase.
            if (player.RuntimeId == ownerRuntimeId) return null;
        }

        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead || player.RuntimeId == ownerRuntimeId) continue;
            if (MathF.Abs(player.PositionX - x) > HitRadius || MathF.Abs(player.PositionZ - z) > HitRadius) continue;
            if (y < player.PositionY || y > player.PositionY + 2f) continue;
            return player;
        }
        return null;
    }

    private bool HitsWorld(float x, float y, float z) =>
        _world.GetBlock((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z)) != World.World.AirRuntimeId;

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        if (!_stores.Velocities.TryGet(id, out var vel)) return;

        ViewerReconciliation.Sync(
            identity.ActorUniqueId, online, _replicated,
            peer => ActorInterest.Includes(peer, pos.X, pos.Z),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddProjectile(
                    identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, vel.X, vel.Y, vel.Z);
                _lastProjected[(identity.ActorUniqueId, peer.RuntimeId)] = new ProjectedPosition(pos.X, pos.Y, pos.Z);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
                ReplicatedRemovalCount++;
            });
    }

    private void Remove(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Entities.IsAlive(id)) return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        _stores.DestroyActor(identity.ActorRuntimeId, id);
        RemovalCount++;
        foreach (var peer in online)
        {
            if (_replicated.Remove((identity.ActorUniqueId, peer.RuntimeId)))
            {
                _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
                ReplicatedRemovalCount++;
            }
        }
    }

    private readonly record struct ProjectedPosition(float X, float Y, float Z)
    {
        private const float PositionEpsilonSquared = 0.0001f;

        public bool MeaningfullyChanged(ProjectedPosition previous)
        {
            var dx = X - previous.X;
            var dy = Y - previous.Y;
            var dz = Z - previous.Z;
            return dx * dx + dy * dy + dz * dz > PositionEpsilonSquared;
        }
    }
}

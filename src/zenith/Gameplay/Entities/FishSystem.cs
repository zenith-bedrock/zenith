using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XX, Priority 2 — second non-ground-navigation mob, and a third distinct movement-validity
/// rule (after <see cref="GroundMobMovement.TryMoveHorizontal"/> and Bat's bespoke flight clearance):
/// <see cref="TrySwim"/> requires the destination cell to BE water, not merely have air/support
/// nearby. Combat/loot/XP/despawn/replication all reuse <see cref="DamageableActorCombat"/>/
/// <see cref="DespawnLifecycle"/>/<see cref="ActorInterest"/> unmodified.
///
/// ECS-authoritative since this migration (see docs/decisions.md, the ADR after §131): Position/
/// Health/ActorIdentity/DespawnTracking live in the shared <see cref="EntityRuntime"/> stores; only
/// <see cref="FishState"/> (wander heading) is feature-specific and owned here — same shape as
/// <see cref="SpiderState"/>. Migrating onto <see cref="DamageDispatch"/> also means arrows can now
/// hit a fish, which the old <c>IDamageableActor</c>/<c>FishStore</c> pattern never allowed
/// (<c>ProjectileSystem.FindHitDamageableActor</c> only ever queried ECS component stores).
/// </summary>
sealed class FishSystem : IGameSystem
{
    private const float SpawnOffsetX = 4f;
    private const float MovePerTick = 0.06f;
    private const int MinWanderTicks = 30;
    private const int MaxWanderTicks = 80;
    private const string LootItemName = "minecraft:cod";
    private const int KillExperience = 1;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<FishState> _fish;
    private readonly StackId _lootItem;
    private readonly Random _random;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = new();
    private readonly List<EntityId> _tickScratch = [];
    private bool _bootstrapSpawned;

    public FishSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _fish = new ComponentStore<FishState>(stores.Entities);
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Fish => _fish.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<FishState> FishStates => _fish;

    /// <summary>Cross-species dispatch seam (Phase XXII) — "is this ECS entity a fish," nothing more.</summary>
    internal bool Owns(EntityId id) => _fish.Has(id);
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_fish.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapFish(online);

        _tickScratch.Clear();
        _tickScratch.AddRange(_fish.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;
            Wander(id, clock, online);
            ReconcileViewers(id, online);
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveFishId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
    }

    private bool IsKnownAliveFishId(long actorUniqueId)
    {
        foreach (var id in _fish.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
    }

    /// <summary>
    /// Only spawns where water already exists — a fixed offset from the first online player,
    /// checked directly rather than sampled (unlike every ground mob's <c>SampleSpawnFeetY</c>,
    /// there is no "find the nearest suitable surface" logic here; this is deliberately the
    /// smallest useful vertical slice, not a spawn-placement system). If that cell isn't water,
    /// no fish spawns this tick — acceptable for a vertical slice, not a bug.
    /// </summary>
    private void EnsureBootstrapFish(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _fish.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX + SpawnOffsetX;
        var y = player.PositionY;
        var z = player.PositionZ;
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        if (_world.GetBlock(blockX, blockY, blockZ) != Blocks.Water) return;

        SpawnFish(x, y, z);
        _bootstrapSpawned = true;
    }

    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnFish(float x, float y, float z)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Fish.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(3f) }); // Vanilla-parity: fish are fragile.
        _stores.Velocities.Set(id, new Velocity());
        _stores.Despawn.Set(id, new DespawnTracking());
        _fish.Set(id, new FishState());
        return id;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death. Same shape as every other migrated actor's despawn — position/visibility-based, not water-based.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return false;
        ref var tracking = ref _stores.Despawn.GetRef(id);
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            pos.X, pos.Z, online, DespawnRadius, clock.CurrentTick, tracking.LastSeenNearPlayerTick);
        tracking.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        if (!_stores.Identities.TryGet(id, out var identity)) return false;
        foreach (var peer in online)
            if (_replicated.Remove((identity.ActorUniqueId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
        _stores.DestroyActor(identity.ActorRuntimeId, id);
        DespawnCount++;
        return true;
    }

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        if (!_stores.Health.TryGet(id, out var healthComponent)) return;
        var health = healthComponent.State;

        ViewerReconciliation.Sync(
            identity.ActorUniqueId, online, _replicated,
            peer => ActorInterest.Includes(peer, pos.X, pos.Z),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddFish(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
                peer.Session.Protocol.Entity.SendHealth(identity.ActorRuntimeId, health.Current, health.Maximum);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
                ReplicatedRemovalCount++;
            });
    }

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        const float attackDistance = 2.25f;
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, attackDistance, 4f, currentTick, TryApplyDamage);
    }

    /// <summary>Concrete Fish health/removal operation — same shape as every other migrated actor's, one loot item, no retaliation.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Fish", currentTick,
            destroyActor: fid =>
            {
                if (_stores.Identities.TryGet(fid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, fid);
            },
            onDeathReplicatedToPeer: _ => ReplicatedRemovalCount++);

    /// <summary>3D wander, structurally identical to Bat's — the difference is entirely in <see cref="TrySwim"/>'s validity rule, not in how a heading is picked or walked.</summary>
    private void Wander(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _fish.GetRef(id);
        if (clock.CurrentTick >= state.WanderChangeAtTick)
            PickNewHeading(id, clock);

        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var desiredX = pos.X + state.WanderDirectionX * MovePerTick;
        var desiredY = pos.Y + state.WanderDirectionY * MovePerTick;
        var desiredZ = pos.Z + state.WanderDirectionZ * MovePerTick;
        if (TrySwim(id, desiredX, desiredY, desiredZ))
        {
            ref var p = ref _stores.Positions.GetRef(id);
            p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(state.WanderDirectionX, state.WanderDirectionZ), LookMath.DefaultMaxTurnDegreesPerTick);
            BroadcastMove(id, online);
        }
        else
            state.WanderChangeAtTick = clock.CurrentTick; // out of water — choose a fresh heading next tick
    }

    private void BroadcastMove(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        foreach (var peer in online)
        {
            if (!_replicated.Contains((identity.ActorUniqueId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw(identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, yaw: pos.Yaw, headYaw: pos.Yaw);
        }
    }

    private void PickNewHeading(EntityId id, GameClock clock)
    {
        ref var state = ref _fish.GetRef(id);
        var yaw = _random.NextSingle() * MathF.PI * 2f;
        var horizontal = MathF.Cos(_random.NextSingle() * MathF.PI * 0.5f);
        state.WanderDirectionX = MathF.Cos(yaw) * horizontal;
        state.WanderDirectionZ = MathF.Sin(yaw) * horizontal;
        state.WanderDirectionY = (_random.NextSingle() * 2f - 1f) * (1f - horizontal);
        state.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);
    }

    /// <summary>
    /// The third movement-validity rule (see this system's doc comment): the destination cell
    /// itself must BE water. No support requirement (unlike ground), no mere-air requirement
    /// (unlike flight) — occupancy of a specific block type is the entire rule.
    /// </summary>
    private bool TrySwim(EntityId id, float x, float y, float z)
    {
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        if (_world.GetBlock(blockX, blockY, blockZ) != Blocks.Water) return false;

        ref var p = ref _stores.Positions.GetRef(id);
        p.X = x;
        p.Y = y;
        p.Z = z;
        return true;
    }
}

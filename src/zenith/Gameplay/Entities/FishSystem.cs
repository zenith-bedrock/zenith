using Zenith.Gameplay.Ports;
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
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<FishState> _fish;
    private readonly MobCore _mob;
    private readonly Random _random;
    private readonly ActorReplication _replication;
    private readonly List<EntityId> _tickScratch = [];

    public FishSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _mob = new MobCore(MobDefinitions.Fish, world, players, stores, itemPalette, trackPoses: false, withVelocity: true);
        _replication = _mob.Replication;
        _fish = new ComponentStore<FishState>(stores.Entities);
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Fish => _fish.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<FishState> FishStates => _fish;

    /// <summary>Cross-species dispatch seam (Phase XXII) — "is this ECS entity a fish," nothing more.</summary>
    internal bool Owns(EntityId id) => _fish.Has(id);
    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long DespawnCount => _replication.DespawnCount;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;

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

        _replication.Prune(_fish.Entities, online);
    }


    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnFish(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
        _fish.Set(id, new FishState());
        return id;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death. Same shape as every other migrated actor's despawn — position/visibility-based, not water-based.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online) =>
        _replication.TryDespawn(id, clock, online, DespawnRadius);

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online) =>
        _replication.Reconcile(id, online);

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        const float attackDistance = 2.25f;
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, attackDistance, 4f, currentTick, TryApplyDamage);
    }

    /// <summary>Concrete Fish health/removal operation — same shape as every other migrated actor's, one loot item, no retaliation.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.TryApplyDamage(id, source, amount, online, currentTick);

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
            if (!_replication.Viewers.Contains((identity.ActorUniqueId, peer.RuntimeId))) continue;
            peer.Client.Entity.SendMoveActorAbsoluteRaw(identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, yaw: pos.Yaw, headYaw: pos.Yaw);
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

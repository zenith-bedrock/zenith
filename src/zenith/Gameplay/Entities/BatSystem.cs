using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// The flying mob. Position, health, identity and despawn tracking are the shared <see cref="EntityRuntime"/> components and
/// showing it to players is the shared <see cref="ActorReplication"/>; only <see cref="BatState"/> (its 3D wander heading) and the
/// flight rule are its own. Movement is bespoke 3D flight, not <see cref="GroundMobMovement"/>: a bat has no supporting-block
/// requirement and wanders on a vertical axis no ground mob uses.
/// </summary>
sealed class BatSystem : IGameSystem
{
    private const float MovePerTick = 0.1f;
    private const int MinWanderTicks = 30;
    private const int MaxWanderTicks = 80;
    private const float WanderVerticalBias = 0.5f; // Keeps the random heading from drifting mostly sideways.
    private const float AttackDamage = 4f;
    private const float AttackDistance = 2.25f;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<BatState> _bats;
    private readonly MobCore _mob;
    private readonly ActorReplication _replication;
    private readonly Random _random;
    private readonly List<EntityId> _tickScratch = [];

    public BatSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _stores = stores;
        _mob = new MobCore(MobDefinitions.Bat, world, players, stores, itemPalette, withVelocity: false);
        _replication = _mob.Replication;
        _bats = new ComponentStore<BatState>(stores.Entities);
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Bats => _bats.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<BatState> BatStates => _bats;

    /// <summary>Cross-species dispatch seam — "is this ECS entity a bat," nothing more.</summary>
    internal bool Owns(EntityId id) => _bats.Has(id);
    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedMoveCount => _replication.MoveCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long DespawnCount => _replication.DespawnCount;

    internal EntityId SpawnBat(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
        _bats.Set(id, new BatState());
        return id;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _replication.BeginTick(online);
        if (online.Count == 0) return;

        _tickScratch.Clear();
        _tickScratch.AddRange(_bats.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (_replication.TryDespawn(id, clock, online, DespawnRadius)) continue;
            _replication.Reconcile(id, online);
            DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, AttackDamage, clock.CurrentTick, TryApplyDamage);
            if (!_stores.Entities.IsAlive(id)) continue;
            Wander(id, clock);
            _replication.Reconcile(id, online);
        }

        _replication.Prune(_bats.Entities, online);
        _replication.ReplicateMoves(_bats.Entities, online);
    }

    /// <summary>Concrete Bat health/removal operation: one loot item, no retaliation.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.TryApplyDamage(id, source, amount, online, currentTick);

    /// <summary>
    /// Bespoke 3D wander: picks a random heading including a vertical component, walks it, retries sooner if blocked. The one real
    /// behavioural difference from every ground mob's wander is the Y axis being live, not fixed to a sampled ground height.
    /// </summary>
    private void Wander(EntityId id, GameClock clock)
    {
        if (clock.CurrentTick >= _bats.GetRef(id).WanderChangeAtTick)
            PickNewHeading(id, clock);

        var state = _bats.GetRef(id);
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (TryFly(id, pos.X + state.WanderDirectionX * MovePerTick, pos.Y + state.WanderDirectionY * MovePerTick, pos.Z + state.WanderDirectionZ * MovePerTick))
        {
            ref var p = ref _stores.Positions.GetRef(id);
            p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(state.WanderDirectionX, state.WanderDirectionZ), LookMath.DefaultMaxTurnDegreesPerTick);
        }
        else
            _bats.GetRef(id).WanderChangeAtTick = clock.CurrentTick; // blocked — choose a fresh heading next tick
    }

    private void PickNewHeading(EntityId id, GameClock clock)
    {
        ref var state = ref _bats.GetRef(id);
        var yaw = _random.NextSingle() * MathF.PI * 2f;
        var horizontal = MathF.Cos(_random.NextSingle() * MathF.PI * WanderVerticalBias);
        state.WanderDirectionX = MathF.Cos(yaw) * horizontal;
        state.WanderDirectionZ = MathF.Sin(yaw) * horizontal;
        state.WanderDirectionY = (_random.NextSingle() * 2f - 1f) * (1f - horizontal);
        state.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);
    }

    /// <summary>
    /// Flight-validity probe: a flying mob needs body clearance but, unlike every ground mob, no solid block underneath.
    /// </summary>
    private bool TryFly(EntityId id, float x, float y, float z)
    {
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        if (_world.GetBlock(blockX, blockY, blockZ) != World.World.AirRuntimeId ||
            _world.GetBlock(blockX, blockY + 1, blockZ) != World.World.AirRuntimeId)
            return false;

        ref var p = ref _stores.Positions.GetRef(id);
        p.X = x;
        p.Y = y;
        p.Z = z;
        return true;
    }
}

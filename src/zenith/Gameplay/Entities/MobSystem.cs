using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>What <see cref="DamageDispatch"/> needs from a species: whether it owns an entity, and how it takes damage.</summary>
interface IDamageableSpecies
{
    bool Owns(EntityId id);

    bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick);
}

/// <summary>
/// The life cycle every ECS mob species shares (ADR §173): one pass over its entities per tick that despawns the ones nobody is
/// near, keeps viewers in step, applies players' attacks, and then hands each survivor to <see cref="Think"/>, which is all a species
/// writes of its own, plus its spawn composition. Afterwards viewers of departed actors are pruned and moves are replicated.
/// A species keeps its state component (<typeparamref name="TState"/>), its AI and its attacks; spawning, damage, loot, replication
/// and the <see cref="DamageDispatch"/> seam come from here and from <see cref="MobCore"/>.
/// </summary>
abstract class MobSystem<TState> : IGameSystem, IDamageableSpecies where TState : struct
{
    /// <summary>Despawn if no player is this close: larger than any species' detection range, so a chasing mob is never cut off.</summary>
    protected const float DefaultDespawnRadius = 64f;

    protected readonly World.World _world;
    protected readonly PlayerManager _players;
    protected readonly EntityRuntime _stores;
    protected readonly MobCore _mob;
    protected readonly ActorReplication _replication;

    /// <summary>The species' own state component, one per entity.</summary>
    protected readonly ComponentStore<TState> States;

    // Reused each tick: a despawn or death mid-pass changes the store, so the pass walks a stable copy without allocating one.
    private readonly List<EntityId> _tickScratch = [];

    protected MobSystem(
        MobDefinition definition, World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette,
        bool trackPoses = true, bool withVelocity = true, Random? random = null)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _mob = new MobCore(definition, world, players, stores, itemPalette, trackPoses, withVelocity, random);
        _replication = _mob.Replication;
        States = new ComponentStore<TState>(stores.Entities);
    }

    /// <summary>How far from every player an actor may be before it is despawned (a lifecycle removal: no loot, no experience).</summary>
    protected virtual float DespawnRadius => DefaultDespawnRadius;

    /// <summary>False for a species whose moves are not streamed to viewers by this pass (it teleports, or replicates itself).</summary>
    protected virtual bool ReplicatesMoves => true;

    /// <summary>True when the species should keep running with nobody online (nothing in the shipped species does).</summary>
    protected virtual bool RunsWithoutPlayers => false;

    internal IReadOnlyList<EntityId> Entities => States.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<TState> StateStore => States;

    /// <summary>Cross-species dispatch seam: "is this ECS entity one of mine," nothing more.</summary>
    public bool Owns(EntityId id) => States.Has(id);

    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedMoveCount => _replication.MoveCount;
    internal long ReplicatedMoveSkippedCount => _replication.MoveSkippedCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long DespawnCount => _replication.DespawnCount;

    /// <summary>One entity's turn: AI, movement, attacks. It is alive, in view of the right peers and has had its damage applied.</summary>
    protected abstract void Think(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online);

    /// <summary>Once per tick after every entity has thought, before viewers are pruned (breeding, for instance).</summary>
    protected virtual void AfterThinking(GameClock clock, IReadOnlyList<Player.Player> online) { }

    /// <summary>Called first every tick, even with nobody online.</summary>
    protected virtual void BeginTick(GameClock clock) { }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _replication.BeginTick(online);
        BeginTick(clock);
        if (online.Count == 0 && !RunsWithoutPlayers) return;

        _tickScratch.Clear();
        _tickScratch.AddRange(States.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (_replication.TryDespawn(id, clock, online, DespawnRadius)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;

            Think(id, clock, online);
            if (_stores.Entities.IsAlive(id)) ReconcileViewers(id, online);
        }

        AfterThinking(clock, online);
        _replication.Prune(States.Entities, online);
        if (ReplicatesMoves) _replication.ReplicateMoves(States.Entities, online);
    }

    /// <summary>Brings the actor's viewers in line with who is near; a species that must tell a newcomer more overrides this.</summary>
    protected virtual void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online) => _replication.Reconcile(id, online);

    /// <summary>The composition step on top of <see cref="MobCore.Spawn"/>: attaches the species' state component.</summary>
    protected EntityId SpawnWith(float x, float y, float z, TState state = default)
    {
        var id = _mob.Spawn(x, y, z);
        States.Set(id, state);
        return id;
    }

    /// <summary>Players attacking this actor. A species that reacts to being hit overrides <see cref="TryApplyDamage"/> instead.</summary>
    protected virtual void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.ApplyPlayerAttacks(id, online, currentTick, TryApplyDamage);

    /// <summary>Damage, then (on a kill) loot, experience, replication and destruction; a species may react first and call base.</summary>
    public virtual bool TryApplyDamage(
        EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.TryApplyDamage(id, source, amount, online, currentTick);
}

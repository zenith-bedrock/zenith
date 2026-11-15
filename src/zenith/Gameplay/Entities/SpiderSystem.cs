using Zenith.Gameplay.Ports;
using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXII — sixth ECS-authoritative actor. Position/Health/ActorIdentity/DespawnTracking live
/// in <see cref="EntityRuntime"/>'s shared component stores; only <see cref="SpiderState"/>
/// (retained target, attack cooldown) is feature-specific and owned here — same shape as
/// <see cref="ZombieState"/>, kept separate for the same reason described there. Spider never had
/// knockback (that stayed Zombie-only since Phase XVI), so nothing here reads or writes
/// <c>Velocity.X</c>/<c>Velocity.Z</c> — but Phase XXIX attaches the component anyway and reuses
/// <c>Velocity.Y</c> as every ground mob's gravity fall-speed.
///
/// <see cref="TryApplyPoison"/> is unchanged by this migration — it always operated on the target
/// <em>Player</em>'s <c>Effects</c> dictionary, never on Spider's own state, so ECS migration had
/// nothing to touch there. This is the concrete proof that a feature-specific post-hit consequence
/// (poison) stays feature-owned and does not need to become part of the ECS damage seam.
/// </summary>
sealed class SpiderSystem : IGameSystem
{
    private const float SpawnDistance = 6f;
    private const float DetectionDistance = 20f;
    private float AttackDistance => _mob.Definition.Melee!.Reach;
    private const float MovePerTick = 0.11f; // Faster than Zombie's 0.08 — a real behavioral difference, not just renaming.
    private const float DespawnRadius = 64f;
    private const float PoisonChance = 0.3f;
    private const int PoisonDurationTicks = 100; // 5s @ 20 TPS — same cadence EffectSystem already ticks Poison at.

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<SpiderState> _spiders;
    private readonly MobCore _mob;
    private readonly GroundMobBody _body;
    private readonly Random _random;
    private readonly ActorReplication _replication;
    private readonly List<EntityId> _tickScratch = []; // Reused per tick — see ZombieSystem's identical field for why.

    public SpiderSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _mob = new MobCore(MobDefinitions.Spider, world, players, stores, itemPalette);
        _replication = _mob.Replication;
        _body = new GroundMobBody(world, stores, players);
        _spiders = new ComponentStore<SpiderState>(stores.Entities);
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Spiders => _spiders.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<SpiderState> SpiderStates => _spiders;

    /// <summary>Cross-species dispatch seam (Phase XXI/XXII) — "is this ECS entity a spider," nothing more.</summary>
    internal bool Owns(EntityId id) => _spiders.Has(id);
    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedMoveCount => _replication.MoveCount;
    internal long ReplicatedMoveSkippedCount => _replication.MoveSkippedCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long DespawnCount => _replication.DespawnCount;
    internal long PoisonInflictedCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _replication.BeginTick(online);
        if (online.Count == 0) return;

        _tickScratch.Clear();
        _tickScratch.AddRange(_spiders.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;
            var target = _body.FindOrAcquireTarget(id, online, ref _spiders.GetRef(id).TargetPlayerRuntimeId, DetectionDistance);
            if (target is not null)
                _body.ChaseTarget(id, target, DetectionDistance, AttackDistance, MovePerTick);
            TryAttackPlayer(id, target, clock, online);
            _body.ApplyGravity(id);
            ReconcileViewers(id, online);
        }

        _replication.Prune(_spiders.Entities, online);
        _replication.ReplicateMoves(_spiders.Entities, online);
    }


    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnSpider(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
        _spiders.Set(id, new SpiderState());
        return id;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online) =>
        _replication.TryDespawn(id, clock, online, DespawnRadius);

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online) =>
        _replication.Reconcile(id, online);

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.ApplyPlayerAttacks(id, online, currentTick, TryApplyDamage);

    private void TryAttackPlayer(EntityId id, Player.Player? target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (target is null || !_mob.IsMeleeReady(id, clock.CurrentTick)) return;
        if (!_stores.Positions.TryGet(id, out var pos) || !GroundMobBody.IsTargetValid(pos, target, DetectionDistance, out _)) return;
        if (_mob.TryMeleeAttack(id, target, clock.CurrentTick, online))
            TryApplyPoison(target, clock);
    }

    /// <summary>
    /// The mob side of a status effect: writes the same <c>Player.Effects.Active</c> entry a potion or
    /// intent would (see <see cref="EffectSystem"/>'s <c>ApplyPendingIntent</c>), then replicates
    /// the same <see cref="MobEffectPacket"/>. Unchanged by ECS migration — see this class's doc
    /// comment.
    /// </summary>
    private void TryApplyPoison(Player.Player target, GameClock clock)
    {
        if (target.IsDead) return;
        if (_random.NextDouble() >= PoisonChance) return;

        var expiresAtTick = clock.CurrentTick + PoisonDurationTicks;
        if (!target.Effects.ApplyOrRefreshEffect(EffectType.Poison, 0, expiresAtTick, clock.CurrentTick)) return; // a longer poison is already active
        target.Client.Entity.SendMobEffect(
            (ulong)target.RuntimeId, MobEffectPacket.EventAdd, (int)EffectType.Poison, 0, showParticles: true, PoisonDurationTicks);
        PoisonInflictedCount++;
    }

    /// <summary>Concrete Spider health/removal operation — same shape as every other migrated actor's.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.TryApplyDamage(id, source, amount, online, currentTick);






}

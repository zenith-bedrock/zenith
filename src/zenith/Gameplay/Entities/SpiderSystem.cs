using Zenith.Gameplay.Ports;
using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
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
[GameSystem("spider")]
sealed class SpiderSystem : MobSystem<SpiderState>
{
    private const float SpawnDistance = 6f;
    private const float DetectionDistance = 20f;
    private float AttackDistance => _mob.Definition.Melee!.Reach;
    private const float MovePerTick = 0.11f; // Faster than Zombie's 0.08 — a real behavioral difference, not just renaming.
    private const float PoisonChance = 0.3f;
    private const int PoisonDurationTicks = 100; // 5s @ 20 TPS — same cadence EffectSystem already ticks Poison at.

    private readonly GroundMobBody _body;
    private readonly Random _random;

    public SpiderSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
        : base(MobDefinitions.Spider, world, players, stores, itemPalette)
    {
        _body = new GroundMobBody(world, stores, players);
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Spiders => Entities;
    internal ComponentStore<SpiderState> SpiderStates => States;

    internal long PoisonInflictedCount { get; private set; }

    protected override void Think(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var target = _body.FindOrAcquireTarget(id, online, ref States.GetRef(id).TargetPlayerRuntimeId, DetectionDistance);
        if (target is not null)
            _body.ChaseTarget(id, target, DetectionDistance, AttackDistance, MovePerTick);
        TryAttackPlayer(id, target, clock, online);
        _body.ApplyGravity(id);
    }

    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnSpider(float x, float y, float z) => SpawnWith(x, y, z);

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
            (ulong)target.RuntimeId, EffectChange.Add, (int)EffectType.Poison, 0, showParticles: true, PoisonDurationTicks);
        PoisonInflictedCount++;
    }
}

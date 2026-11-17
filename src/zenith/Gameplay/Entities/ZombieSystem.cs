using Zenith.Gameplay.Ports;
using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXI — first ECS-authoritative actor. Position/Health/Velocity/ActorIdentity/
/// DespawnTracking live in <see cref="EntityRuntime"/>'s shared component stores; only
/// <see cref="ZombieState"/> (retained target, attack cooldown) is feature-specific and owned
/// here. The old <c>Zombie</c> object and <c>ZombieStore</c> are gone — there is exactly one
/// authoritative copy of this data now, not a concrete object mirroring what the components
/// already hold. Combat/loot/XP bookkeeping moved to <see cref="DamageableActorCombat"/> (shared by every species). See docs/history/phases/phase-xxi-ecs-foundation-findings.md.
/// </summary>
[GameSystem("zombie")]
sealed class ZombieSystem : MobSystem<ZombieState>
{
    private const float DetectionDistance = 24f;
    private float AttackDistance => _mob.Definition.Melee!.Reach;
    private const float MovePerTick = 0.08f;

    private readonly GroundMobBody _body;

    public ZombieSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette)
        : base(MobDefinitions.Zombie, world, players, stores, itemPalette)
    {
        _body = new GroundMobBody(world, stores, players);
    }

    internal IReadOnlyList<EntityId> Zombies => Entities;
    internal ComponentStore<ZombieState> ZombieStates => States;

    protected override void Think(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var target = _body.FindOrAcquireTarget(id, online, ref States.GetRef(id).TargetPlayerRuntimeId, DetectionDistance);
        if (target is not null)
            _body.ChaseTarget(id, target, DetectionDistance, AttackDistance, MovePerTick);
        TryAttackPlayer(id, target, clock, online);
        _body.ApplyGravity(id);
    }

    internal EntityId SpawnZombie(float x, float y, float z) => SpawnWith(x, y, z);

    private void TryAttackPlayer(EntityId id, Player.Player? target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (target is null || !_mob.IsMeleeReady(id, clock.CurrentTick)) return;
        if (!_stores.Positions.TryGet(id, out var pos) || !GroundMobBody.IsTargetValid(pos, target, DetectionDistance, out _)) return;
        _mob.TryMeleeAttack(id, target, clock.CurrentTick, online);
    }
}

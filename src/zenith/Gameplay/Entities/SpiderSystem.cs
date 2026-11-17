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
/// A spider's bite is plain melee: in the pack only the cave spider poisons (0, 7 or 15 seconds by difficulty), and the plain spider's
/// data has no effect at all, so this system applies none.
/// </summary>
[GameSystem("spider")]
sealed class SpiderSystem : MobSystem<SpiderState>
{
    private const float SpawnDistance = 6f;
    private const float DetectionDistance = 20f;
    private float AttackDistance => _mob.Definition.Melee!.Reach;
    private const float MovePerTick = 0.11f; // Faster than Zombie's 0.08 — a real behavioral difference, not just renaming.

    private readonly GroundMobBody _body;

    public SpiderSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette)
        : base(MobDefinitions.Spider, world, players, stores, itemPalette)
    {
        _body = new GroundMobBody(world, stores, players);
    }

    internal IReadOnlyList<EntityId> Spiders => Entities;
    internal ComponentStore<SpiderState> SpiderStates => States;

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
        _mob.TryMeleeAttack(id, target, clock.CurrentTick, online);
    }
}

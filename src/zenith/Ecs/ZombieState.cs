namespace Zenith.Ecs;

/// <summary>
/// Phase XXI — Zombie's feature-specific ECS state: exactly the two fields that don't fit a
/// shared component (retained target identity, melee cooldown). Position/Health/Velocity/
/// ActorIdentity/DespawnTracking are all shared components on the same <see cref="EntityId"/> —
/// see docs/history/phases/phase-xxi-ecs-foundation-findings.md for why this split, not a monolithic
/// <c>ZombieComponent</c> holding everything.
/// </summary>
struct ZombieState
{
    /// <summary>Retained target identity — cleared by <c>ZombieSystem</c> when the player becomes invalid, same semantics as the pre-ECS <c>Zombie.TargetPlayerRuntimeId</c>.</summary>
    public long? TargetPlayerRuntimeId;

    /// <summary>Next tick a melee swing may land.</summary>
    public ulong NextAttackTick;
}

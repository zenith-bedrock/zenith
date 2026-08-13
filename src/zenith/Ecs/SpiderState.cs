namespace Zenith.Ecs;

/// <summary>
/// Phase XXII — Spider's feature-specific state: retained target and melee-attack cooldown. Same
/// shape as <c>ZombieState</c> by construction (Spider was always built to mirror Zombie's
/// targeting/chase/attack loop) — kept as a separate type, not shared, because nothing queries
/// "any entity with a retained target" across species; each system reads only its own.
/// </summary>
struct SpiderState
{
    public long? TargetPlayerRuntimeId;
    public ulong NextAttackTick;
}

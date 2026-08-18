namespace Zenith.Ecs;

/// <summary>
/// Golem's feature-specific state: boss phase transition, its two attack cooldowns, and the
/// provoked-target retention (same shape as <see cref="SpiderState"/>'s, re-evaluated every tick
/// against <c>Player.LastVillagerAttack</c> rather than a plain proximity scan). GolemSystem-owned;
/// no meaning outside it.
/// </summary>
struct GolemState
{
    public bool IsEnraged;
    public ulong NextSlamTick;
    public ulong NextAttackTick;
    public long? TargetPlayerRuntimeId;
}

namespace Zenith.Ecs;

/// <summary>
/// Enderman's feature-specific state: damage-triggered aggro (who provoked it, how long the
/// retaliation window lasts, its own attack cooldown) plus the passive-teleport timer used while
/// unaggroed. EndermanSystem-owned; no meaning outside it.
/// </summary>
struct EndermanState
{
    public long? AggroTargetRuntimeId;
    public int AggroTicksRemaining;
    public ulong NextPassiveTeleportTick;
    public ulong NextAttackTick;
}

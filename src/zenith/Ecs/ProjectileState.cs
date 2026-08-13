namespace Zenith.Ecs;

/// <summary>
/// Phase XXI — Projectile's feature-specific ECS state: owner attribution and pure-age lifetime.
/// Deliberately NOT sharing <see cref="DespawnTracking"/> (that family is visibility-reset;
/// Projectile's age is unconditional — see docs/entities.md §7, "Do NOT create fake universal
/// components"). Projectile also has no <see cref="HealthComponent"/> at all — it deals damage,
/// never takes it, proving <see cref="IDamageableActor"/>-shaped data was never the ECS's actual
/// boundary (see docs/history/phases/phase-xxi-ecs-foundation-findings.md).
/// </summary>
struct ProjectileState
{
    public long OwnerRuntimeId;
    public ulong AgeTicks;
}

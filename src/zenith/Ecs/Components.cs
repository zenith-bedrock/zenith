using Zenith.Gameplay.Survival;

namespace Zenith.Ecs;

/// <summary>
/// Phase XXI shared components — introduced only where sharing was already proven by real
/// duplicated data (see docs/history/phases/phase-xxi-ecs-foundation-findings.md, "Initial component model").
/// Deliberately NOT a `Transform`: <see cref="Yaw"/> is one extra field, not a rotation/scale
/// object, because Yaw is the only orientation data any migrated actor (Zombie, Minecart) uses —
/// Projectile leaves it at 0 and never reads it.
/// </summary>
struct Position
{
    public float X;
    public float Y;
    public float Z;
    public float Yaw;
}

/// <summary>
/// Shared velocity *data* — Zombie's knockback decay, Minecart's rolling/friction/rider steering,
/// and Projectile's ballistic integration all read and write the same three floats, but each
/// keeps its own decay/gravity/friction *behavior* in its own system. Sharing the data was
/// justified by real duplication (Phase XVI knockback, Phase XIX/XX Minecart push/roll); forcing
/// the behavior into one system was not, and was explicitly rejected — see docs/ecs.md.
/// </summary>
struct Velocity
{
    public float X;
    public float Y;
    public float Z;
}

/// <summary>
/// One authoritative <see cref="Gameplay.HealthState"/> per migrated damageable actor — the
/// struct itself is just a reference to that existing, already-mature primitive, not a
/// reimplementation. There is exactly one health state per entity: this component, never a
/// leftover mutable field on a concrete actor type.
/// </summary>
struct HealthComponent
{
    public HealthState State;
}

/// <summary>
/// The Bedrock identity a replicated migrated actor needs — deliberately just the two ids, never
/// a packet object or a serializer (see docs/ecs.md, "Replication boundary"). Both are still
/// numerically equal at allocation time (matching every pre-ECS actor's
/// <c>(ulong)entityId</c> convention) but are kept as two fields because that is what
/// <c>AddActorPacket</c>'s wire shape actually requires.
/// </summary>
struct ActorIdentity
{
    public long ActorUniqueId;
    public ulong ActorRuntimeId;
}

/// <summary>
/// Visibility-reset despawn tracking shared by the "ordinary damageable actor" family (Zombie,
/// Minecart) — both call <see cref="Gameplay.DespawnLifecycle.EvaluateDespawn"/> identically.
/// Deliberately NOT shared with Projectile, whose pure-age lifetime is a categorically different
/// rule (see docs/entities.md §7, "Do NOT create fake universal components") — Projectile keeps
/// its own age field in <c>ProjectileState</c> instead of this component.
/// </summary>
struct DespawnTracking
{
    public ulong LastSeenNearPlayerTick;
}

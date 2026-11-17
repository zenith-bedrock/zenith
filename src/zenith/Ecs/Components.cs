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

/// <summary>
/// What is left of a killed actor while its clients play the death animation: the ids the clients know it by, the peers that were
/// told about it, and the tick it is removed. A corpse is its own entity, with none of the dead actor's gameplay components, so no
/// species system, targeting query or damage dispatch can ever see it: it is only presentation, owned by
/// <c>DeathAnimationSystem</c>.
/// </summary>
struct DeathAnimation
{
    public long ActorUniqueId;
    public ulong ActorRuntimeId;
    public ulong RemoveAtTick;
    /// <summary>Runtime ids of the peers that saw the actor die and so must be told to remove it.</summary>
    public long[] ViewerRuntimeIds;
}

/// <summary>
/// When a species with a melee attack (<c>MobDefinition.Melee</c>) may hit again. Shared data: the attack itself is
/// <c>MobCore.TryMeleeAttack</c>, so every melee species cools down, hits and swings the same way.
/// </summary>
struct MeleeCooldown
{
    public ulong NextAttackTick;
}

/// <summary>
/// The horizontal push a hit left on an actor, blocks per tick, fading each tick (<c>KnockbackSystem</c>). Separate from
/// <see cref="Velocity"/>, which a species may own for its own movement (a minecart's rolling, a ground mob's fall speed), so a hit
/// never fights a species' own motion model. Only species whose definition has a <c>knockback</c> profile carry it.
/// </summary>
struct Knockback
{
    public float X;
    public float Z;
    /// <summary>Multiplier the push fades by each tick, from the species' <c>KnockbackProfile</c>.</summary>
    public float DecayPerTick;
}

/// <summary>
/// A mob's <see cref="Gameplay.Survival.Body"/>: its attributes and status effects, wrapped around the same <see cref="Gameplay.HealthState"/> the
/// <see cref="HealthComponent"/> holds. Attached when something first needs it (an effect given to the mob), so a mob that never has one pays nothing.
/// </summary>
struct BodyComponent
{
    public Zenith.Gameplay.Survival.Body Body;
}

/// <summary>Marks a mob that wears something on its head (a helmet or a pumpkin): the sun does not set it alight.</summary>
struct Headgear
{
}

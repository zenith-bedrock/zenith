namespace Zenith.Gameplay.Survival;

/// <summary>
/// Reason an authoritative health mutation was requested. It deliberately describes the
/// gameplay cause, not a packet, item, or entity hierarchy.
/// </summary>
enum DamageCause
{
    Generic,
    Fall,
    Void,
    Melee,
    Projectile,
    Starve,
    Magic,
    Drown,
    Lava,
    Explosion,
    Fire,
    Wither,
    Lightning,
    Freezing,
    Contact
}

/// <summary>What each cause is, as properties (Dragonfly's source flags): which mitigations apply to it.</summary>
static class DamageCauseTraits
{
    /// <summary>Armor reduces it. Falls, the void, hunger, magic, drowning, wither and freezing go around armor.</summary>
    public static bool ReducedByArmour(this DamageCause cause) =>
        cause is not (DamageCause.Fall or DamageCause.Void or DamageCause.Starve or DamageCause.Magic or DamageCause.Drown
            or DamageCause.Wither or DamageCause.Freezing);

    /// <summary>The Resistance effect reduces it. The void and hunger ignore it.</summary>
    public static bool ReducedByResistance(this DamageCause cause) => cause is not (DamageCause.Void or DamageCause.Starve);

    /// <summary>Fire Resistance spares it.</summary>
    public static bool IsFire(this DamageCause cause) => cause is DamageCause.Fire or DamageCause.Lava;
}

/// <summary>
/// Immutable context for one damage request. An attacker identity is intentionally absent until
/// a concrete feature needs attribution; a cause is enough for validation, death messaging, and
/// the first melee-capable actor.
/// </summary>
readonly record struct DamageSource(DamageCause Cause, long? OwnerRuntimeId = null)
{
    public static DamageSource Generic => new(DamageCause.Generic);
    public static DamageSource Fall => new(DamageCause.Fall);
    public static DamageSource Void => new(DamageCause.Void);
    public static DamageSource Starve => new(DamageCause.Starve);
    public static DamageSource Magic => new(DamageCause.Magic);
    public static DamageSource Drown => new(DamageCause.Drown);
    public static DamageSource Lava => new(DamageCause.Lava);
    public static DamageSource Fire => new(DamageCause.Fire);
    public static DamageSource Wither => new(DamageCause.Wither);
    public static DamageSource Lightning => new(DamageCause.Lightning);
    public static DamageSource Freezing => new(DamageCause.Freezing);
    public static DamageSource Contact => new(DamageCause.Contact);
    public static DamageSource Explosion => new(DamageCause.Explosion);
    public static DamageSource Melee => new(DamageCause.Melee);
    public static DamageSource MeleeFrom(long ownerRuntimeId) => new(DamageCause.Melee, ownerRuntimeId);
    /// <summary>Concrete first attribution case; the id is not an actor abstraction.</summary>
    public static DamageSource Projectile(long ownerRuntimeId) => new(DamageCause.Projectile, ownerRuntimeId);

    /// <summary>Bedrock's currently supported DeathInfo vocabulary.</summary>
    public string DeathInfoCause => Cause == DamageCause.Fall ? "fall" : "generic";
}

/// <summary>Outcome of one attempted health mutation on the authoritative gameplay owner.</summary>
enum DamageResultKind
{
    Rejected,
    Applied,
    Died,
    AlreadyDead
}

readonly record struct DamageResult(
    DamageResultKind Kind,
    DamageSource Source,
    float PreviousHealth,
    float CurrentHealth,
    float Absorbed = 0f)
{
    public bool WasApplied => Kind is DamageResultKind.Applied or DamageResultKind.Died;
    public bool CausedDeath => Kind == DamageResultKind.Died;
}

/// <summary>
/// Small, composition-friendly authoritative health state. The owning gameplay execution
/// context is its sole writer; this class provides no locking or packet behaviour.
/// </summary>
sealed class HealthState
{
    public HealthState(float maximum)
    {
        if (!float.IsFinite(maximum) || maximum <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        BaseMaximum = maximum;
        Maximum = maximum;
        Current = maximum;
    }

    /// <summary>
    /// Vanilla hit-invulnerability window (10 ticks / 0.5s — Java's <c>hurtResistantTime</c>, mirrored
    /// by PocketMine's default <c>Living::getAttackCooldown()</c>). Phase XXIII-B real-client-review
    /// finding: Zenith had NO invulnerability concept at all anywhere (player or mob) — every damage
    /// source applied in full every tick with no grace window, so e.g. two mobs hitting the same tick,
    /// or a mob whose AI happens to re-check every tick, could stack far beyond vanilla feel. A
    /// strictly-greater new hit still lands (matches Java's "harder hit overrides" rule) — this is not
    /// a hard damage-immunity, just a duplicate/lesser-hit suppressor.
    /// </summary>
    private const int InvulnerabilityTicks = 10;

    /// <summary>The maximum with no modifier (what the body is born with); <see cref="Maximum"/> is what the health attribute makes of it now.</summary>
    public float BaseMaximum { get; }
    public float Maximum { get; private set; }
    public float Current { get; private set; }

    /// <summary>The shield that damage drains before health (the <c>minecraft:absorption</c> attribute).</summary>
    public float Absorption { get; private set; }
    public bool IsDead { get; private set; }
    public DamageSource? FatalSource { get; private set; }

    private ulong _invulnerableUntilTick;
    private float _lastDamageTaken;

    /// <summary>
    /// Applies one finite, positive damage amount. A lethal transition is emitted exactly once;
    /// later damage cannot re-run death effects or change the recorded fatal source.
    /// <paramref name="currentTick"/> drives the hit-invulnerability window — Void bypasses it
    /// (matches vanilla: falling out of the world damages every tick, not just once). A strictly
    /// harder hit that overrides an active window applies only its delta over the hit that opened
    /// the window, not the full amount again (ADR §98 Adendo).
    /// </summary>
    public DamageResult Apply(DamageSource source, float amount, ulong currentTick)
    {
        var previous = Current;
        if (!float.IsFinite(amount) || amount <= 0f)
            return new DamageResult(DamageResultKind.Rejected, source, previous, previous);

        if (IsDead)
            return new DamageResult(DamageResultKind.AlreadyDead, source, previous, previous);

        var withinActiveWindow = source.Cause != DamageCause.Void && currentTick < _invulnerableUntilTick;
        if (withinActiveWindow && amount <= _lastDamageTaken)
            return new DamageResult(DamageResultKind.Rejected, source, previous, previous);

        // A strictly-harder hit that overrides an active window only applies the delta beyond what
        // the hit that opened the window already accounted for (Dragonfly: damageLeft -= p.lastDamage;
        // PocketMine: MODIFIER_PREVIOUS_DAMAGE_COOLDOWN; vanilla: actuallyHurt(source, amount - lastHurt)).
        // Applying the full new amount on top of an already-suffered overlapping hit would double-count
        // the shared portion. Void bypasses the window entirely and always applies its full amount.
        var appliedAmount = withinActiveWindow ? amount - _lastDamageTaken : amount;

        _lastDamageTaken = amount;
        _invulnerableUntilTick = currentTick + InvulnerabilityTicks;

        var absorbed = MathF.Min(Absorption, appliedAmount);
        Absorption -= absorbed;
        Current = MathF.Max(0f, Current - (appliedAmount - absorbed));
        if (Current > 0f)
            return new DamageResult(DamageResultKind.Applied, source, previous, Current, absorbed);

        IsDead = true;
        FatalSource = source;
        return new DamageResult(DamageResultKind.Died, source, previous, Current, absorbed);
    }

    /// <summary>A new maximum (Health Boost and friends): never below 1, and health above it comes down to it.</summary>
    public void SetMaximum(float maximum)
    {
        Maximum = MathF.Max(1f, maximum);
        if (Current > Maximum) Current = Maximum;
    }

    public void SetAbsorption(float absorption) => Absorption = MathF.Max(0f, absorption);

    /// <summary>Well-fed regeneration (Phase XI.1). No-op once dead; never exceeds Maximum.</summary>
    public void Heal(float amount)
    {
        if (IsDead || !float.IsFinite(amount) || amount <= 0f) return;
        Current = MathF.Min(Maximum, Current + amount);
    }

    /// <summary>
    /// Phase XXV — hydrate from persisted storage on login, not a gameplay damage/heal transition
    /// (no invulnerability-window bookkeeping, no death handling). Caller (world-data load) already
    /// rejects a non-positive persisted value as corrupt, but this clamps defensively regardless —
    /// a hydrated player must never load already-dead.
    /// </summary>
    public void Hydrate(float health)
    {
        Current = Math.Clamp(health, 1f, Maximum);
        IsDead = false;
        FatalSource = null;
    }

    /// <summary>Respawn resets the health lifecycle after its owner completed the transition.</summary>
    public void RestoreFull()
    {
        Absorption = 0f;
        Current = Maximum;
        IsDead = false;
        FatalSource = null;
        _invulnerableUntilTick = 0;
        _lastDamageTaken = 0f;
    }
}

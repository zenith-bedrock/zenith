namespace Zenith.Gameplay;

/// <summary>
/// Reason an authoritative health mutation was requested. It deliberately describes the
/// gameplay cause, not a packet, item, or entity hierarchy.
/// </summary>
enum DamageCause
{
    Generic,
    Fall,
    Void,
    Melee
}

/// <summary>
/// Immutable context for one damage request. An attacker identity is intentionally absent until
/// a concrete feature needs attribution; a cause is enough for validation, death messaging, and
/// the first melee-capable actor.
/// </summary>
readonly record struct DamageSource(DamageCause Cause)
{
    public static DamageSource Generic => new(DamageCause.Generic);
    public static DamageSource Fall => new(DamageCause.Fall);
    public static DamageSource Void => new(DamageCause.Void);
    public static DamageSource Melee => new(DamageCause.Melee);

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
    float CurrentHealth)
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

        Maximum = maximum;
        Current = maximum;
    }

    public float Maximum { get; }
    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public DamageSource? FatalSource { get; private set; }

    /// <summary>
    /// Applies one finite, positive damage amount. A lethal transition is emitted exactly once;
    /// later damage cannot re-run death effects or change the recorded fatal source.
    /// </summary>
    public DamageResult Apply(DamageSource source, float amount)
    {
        var previous = Current;
        if (!float.IsFinite(amount) || amount <= 0f)
            return new DamageResult(DamageResultKind.Rejected, source, previous, previous);

        if (IsDead)
            return new DamageResult(DamageResultKind.AlreadyDead, source, previous, previous);

        Current = MathF.Max(0f, Current - amount);
        if (Current > 0f)
            return new DamageResult(DamageResultKind.Applied, source, previous, Current);

        IsDead = true;
        FatalSource = source;
        return new DamageResult(DamageResultKind.Died, source, previous, Current);
    }

    /// <summary>Respawn resets the health lifecycle after its owner completed the transition.</summary>
    public void RestoreFull()
    {
        Current = Maximum;
        IsDead = false;
        FatalSource = null;
    }
}

namespace Zenith.Gameplay.Survival;

/// <summary>
/// The active status effects of one body (a player or a mob). "Stronger wins": a running effect is kept unless the new instance has a higher
/// amplifier, or the same amplifier and more time left. Pure data; <see cref="Body"/> is what applies it to the attributes.
/// </summary>
sealed class EffectSet
{
    private readonly Dictionary<EffectType, ActiveEffect> _active = [];

    public IReadOnlyDictionary<EffectType, ActiveEffect> Active => _active;

    public bool ApplyOrRefresh(EffectType type, int amplifier, ulong expiresAtTick, ulong currentTick)
    {
        if (_active.TryGetValue(type, out var existing) && !existing.HasExpired(currentTick))
        {
            if (existing.Amplifier > amplifier) return false;
            if (existing.Amplifier == amplifier && existing.ExpiresAtTick >= expiresAtTick) return false;
        }

        _active[type] = new ActiveEffect(type, amplifier, expiresAtTick);
        return true;
    }

    /// <summary>The level of an active effect (amplifier + 1), 0 when it is not active.</summary>
    public int LevelOf(EffectType type) => _active.TryGetValue(type, out var effect) ? effect.Amplifier + 1 : 0;

    public bool Remove(EffectType type) => _active.Remove(type);

    public bool Clear()
    {
        if (_active.Count == 0) return false;
        _active.Clear();
        return true;
    }
}

/// <summary>
/// What a living thing is, for the rules: its health (the one shared <see cref="HealthState"/>), its attributes with their modifier stacks
/// and its effects. Players and mobs have one alike, so an effect given to a zombie is the same code as one given to a player. Whenever the
/// effects change, <see cref="Sync"/> rebuilds the effect-sourced modifiers from <see cref="EffectTable"/> and pushes the results (maximum
/// health, absorption) onto the health state. Single writer: whoever owns the body's tick (the effect system for a player, the mob-effect
/// system for a mob).
/// </summary>
sealed class Body
{
    public Body(HealthState health)
    {
        Health = health;
        Attributes = new AttributeSet(health.BaseMaximum);
    }

    public HealthState Health { get; }
    public AttributeSet Attributes { get; }
    public EffectSet Effects { get; } = new();

    /// <summary>Applies an effect (see <see cref="EffectSet.ApplyOrRefresh"/>); false when a stronger or longer one is already running.</summary>
    public bool Give(EffectType type, int amplifier, ulong expiresAtTick, ulong currentTick)
    {
        if (!Effects.ApplyOrRefresh(type, amplifier, expiresAtTick, currentTick)) return false;
        Sync(type);
        return true;
    }

    public bool Remove(EffectType type)
    {
        if (!Effects.Remove(type)) return false;
        Sync();
        return true;
    }

    public bool Clear()
    {
        if (!Effects.Clear()) return false;
        Sync();
        return true;
    }

    /// <summary>
    /// Rebuilds what the effects give. <paramref name="applied"/> is the effect that was just given: an Absorption effect fills the shield to
    /// its new level when it is applied; otherwise the shield only ever shrinks to what the running effects allow.
    /// </summary>
    public void Sync(EffectType? applied = null)
    {
        Attributes.RemoveFrom(EffectTable.ModifierPrefix);
        var table = EffectTable.Default;
        var absorptionCap = 0f;
        var refill = false;
        foreach (var (type, effect) in Effects.Active)
        {
            var definition = table.Of(type);
            var level = effect.Amplifier + 1;
            foreach (var (attribute, operation, amount) in definition.Modifiers)
                Attributes[attribute].Set(new AttributeModifier(EffectTable.ModifierPrefix + definition.Name, amount * level, operation));
            absorptionCap += definition.Absorption * level;
            if (applied == type && definition.Absorption > 0f) refill = true;
        }

        Health.SetMaximum(Attributes.Value(Attr.Health));
        Health.SetAbsorption(refill ? absorptionCap : MathF.Min(Health.Absorption, absorptionCap));
    }
}

namespace Zenith.Gameplay.Survival;

/// <summary>How a modifier combines with the base (the pack's and the wire's three operations).</summary>
enum AttributeOperation { Add, MultiplyBase, MultiplyTotal }

/// <summary>One change to an attribute. <paramref name="Id"/> names its source (<c>effect:strength</c>, later sprint or an item), so taking it away is exact.</summary>
readonly record struct AttributeModifier(string Id, float Amount, AttributeOperation Operation);

/// <summary>The attribute names of the pack (<c>minecraft:health</c> and the rest), one place so a typo cannot hide.</summary>
static class Attr
{
    public const string Health = "minecraft:health";
    public const string Absorption = "minecraft:absorption";
    public const string Movement = "minecraft:movement";
    public const string AttackDamage = "minecraft:attack_damage";
    public const string KnockbackResistance = "minecraft:knockback_resistance";

    public static readonly IReadOnlyList<string> All = [Health, Absorption, Movement, AttackDamage, KnockbackResistance];
}

/// <summary>
/// One attribute: a base value and a stack of modifiers. The value is the vanilla formula: the base plus every <c>add</c>, then a share of
/// that for each <c>multiply_base</c>, then times (1 + amount) of each <c>multiply_total</c>, clamped to the attribute's range.
/// </summary>
sealed class AttributeInstance(string name, float baseValue, float min, float max)
{
    private readonly List<AttributeModifier> _modifiers = [];

    public string Name { get; } = name;
    public float Base { get; } = baseValue;
    public float Min { get; } = min;
    public float Max { get; } = max;
    public IReadOnlyList<AttributeModifier> Modifiers => _modifiers;

    public float Value => Math.Clamp(Unclamped, Min, Max);

    /// <summary>The formula's result before the range clamps it (a rule that wants the net bonus, like a melee hit, reads this).</summary>
    public float Unclamped
    {
        get
        {
            var added = Base;
            foreach (var m in _modifiers)
                if (m.Operation == AttributeOperation.Add) added += m.Amount;
            var value = added;
            foreach (var m in _modifiers)
                if (m.Operation == AttributeOperation.MultiplyBase) value += added * m.Amount;
            foreach (var m in _modifiers)
                if (m.Operation == AttributeOperation.MultiplyTotal) value *= 1f + m.Amount;
            return value;
        }
    }

    /// <summary>Adds the modifier, replacing the one with the same id.</summary>
    public void Set(AttributeModifier modifier)
    {
        var index = _modifiers.FindIndex(m => m.Id == modifier.Id);
        if (index >= 0) _modifiers[index] = modifier;
        else _modifiers.Add(modifier);
    }

    public bool Remove(string id) => _modifiers.RemoveAll(m => m.Id == id) > 0;

    /// <summary>Removes every modifier whose id starts with <paramref name="prefix"/> (all of one kind of source).</summary>
    public int RemoveFrom(string prefix) => _modifiers.RemoveAll(m => m.Id.StartsWith(prefix, StringComparison.Ordinal));
}

/// <summary>The attributes of one body, by BDS name.</summary>
sealed class AttributeSet
{
    private readonly Dictionary<string, AttributeInstance> _byName = new(StringComparer.Ordinal);

    public AttributeSet(float baseHealth)
    {
        Add(new AttributeInstance(Attr.Health, baseHealth, 1f, 1024f));
        Add(new AttributeInstance(Attr.Absorption, 0f, 0f, 1024f));
        Add(new AttributeInstance(Attr.Movement, 0.1f, 0f, float.MaxValue));
        Add(new AttributeInstance(Attr.AttackDamage, 1f, 0f, 2048f));
        Add(new AttributeInstance(Attr.KnockbackResistance, 0f, 0f, 1f));
    }

    private void Add(AttributeInstance attribute) => _byName[attribute.Name] = attribute;

    public bool TryGet(string name, out AttributeInstance attribute) => _byName.TryGetValue(name, out attribute!);

    public AttributeInstance this[string name] => _byName[name];

    public float Value(string name) => _byName[name].Value;

    /// <summary>Takes every modifier of every attribute that came from <paramref name="prefix"/>.</summary>
    public void RemoveFrom(string prefix)
    {
        foreach (var attribute in _byName.Values) attribute.RemoveFrom(prefix);
    }
}

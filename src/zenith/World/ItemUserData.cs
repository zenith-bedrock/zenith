namespace Zenith.World;

/// <summary>
/// The per-stack state an item carries besides what it is (ADR §148): what BDS keeps in the stack's user-data tag.
/// Today only <see cref="Damage"/> (the <c>Damage</c> tag of a durable item: points of wear, 0 = new). Enchantments,
/// custom names and firework colours belong here too when they are modelled. It is part of <see cref="StackId"/> because
/// two stacks that differ in it are not the same stack (they never merge), exactly as for aux — but it is a value of its
/// own, never folded into aux: aux is a variant (bed colour), user data is state that changes with use.
/// </summary>
readonly record struct ItemUserData
{
    /// <summary>Largest damage a stack can carry (generous over the real maximum, 2031 for netherite tools).</summary>
    public const int MaxDamage = short.MaxValue;

    public int Damage { get; }

    public ItemUserData(int damage)
    {
        if (damage < 0 || damage > MaxDamage)
            throw new ArgumentOutOfRangeException(nameof(damage), damage, $"damage must be 0..{MaxDamage}");
        Damage = damage;
    }

    /// <summary>A stack with no user data: what every freshly made item has.</summary>
    public bool IsDefault => Damage == 0;
}

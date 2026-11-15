namespace Zenith.Gameplay.Loot;

/// <summary>An inclusive integer range a count or a roll count is drawn from.</summary>
readonly record struct LootRange(int Min, int Max)
{
    public static LootRange Exactly(int value) => new(value, value);

    public int Draw(Random random) => Min == Max ? Min : random.Next(Min, Max + 1);
}

/// <summary>What a pool or entry may ask about the kill. Grows only with a feature that can answer it.</summary>
readonly record struct LootContext(bool KilledByPlayer);

/// <summary>A condition a pool must satisfy before it rolls: the kill came from a player, or a flat chance.</summary>
abstract record LootCondition
{
    public abstract bool Holds(in LootContext context, Random random);
}

sealed record KilledByPlayerCondition : LootCondition
{
    public override bool Holds(in LootContext context, Random random) => context.KilledByPlayer;
}

sealed record RandomChanceCondition(double Chance) : LootCondition
{
    public override bool Holds(in LootContext context, Random random) => random.NextDouble() < Chance;
}

/// <summary>One thing a pool can award: an item, its pick weight and the count it is awarded in.</summary>
sealed record LootEntry(string Item, int Weight, LootRange Count);

/// <summary>A group of entries that is rolled <see cref="Rolls"/> times, picking one weighted entry per roll, if every condition holds.</summary>
sealed record LootPool(LootRange Rolls, IReadOnlyList<LootCondition> Conditions, IReadOnlyList<LootEntry> Entries);

/// <summary>
/// What a destroyed or interacted object awards, as data. Generation only: it says which items and how many, and knows nothing
/// of where they land (the floor-drop store, an inventory) or how they are simulated.
/// </summary>
sealed record LootTable(string Name, IReadOnlyList<LootPool> Pools)
{
    /// <summary>Every item name this table can award, for validating against the item catalog.</summary>
    public IEnumerable<string> Items => Pools.SelectMany(pool => pool.Entries).Select(entry => entry.Item);

    /// <summary>One award: an item and how many of it.</summary>
    public readonly record struct Award(string Item, int Count);

    /// <summary>Rolls the table: each pool whose conditions hold picks entries by weight, and an entry's count is drawn from its range. Zero counts award nothing.</summary>
    public List<Award> Roll(LootContext context, Random random)
    {
        var awards = new List<Award>();
        foreach (var pool in Pools)
        {
            if (!pool.Conditions.All(condition => condition.Holds(context, random))) continue;
            var rolls = pool.Rolls.Draw(random);
            for (var i = 0; i < rolls; i++)
            {
                var entry = Pick(pool.Entries, random);
                var count = entry.Count.Draw(random);
                if (count > 0) awards.Add(new Award(entry.Item, count));
            }
        }

        return awards;
    }

    private static LootEntry Pick(IReadOnlyList<LootEntry> entries, Random random)
    {
        var total = 0;
        foreach (var entry in entries) total += entry.Weight;
        var roll = random.Next(total);
        foreach (var entry in entries)
        {
            roll -= entry.Weight;
            if (roll < 0) return entry;
        }

        return entries[^1];
    }
}

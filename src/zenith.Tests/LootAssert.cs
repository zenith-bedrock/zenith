using Zenith.Gameplay.Loot;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>Kill tests assert against the species' loot table, not a hard-coded item: a drop is random within the table's data.</summary>
static class LootAssert
{
    /// <summary>Every item on the floor is one <paramref name="table"/> can award, within its largest count; an empty table drops nothing.</summary>
    public static void OnlyWhatTheTableAwards(World.World world, ItemPalette palette, string table)
    {
        var allowed = LootTables.Get(table).Pools.SelectMany(pool => pool.Entries)
            .GroupBy(entry => palette.Require(entry.Item))
            .ToDictionary(group => (int)group.Key, group => group.Max(entry => entry.Count.Max) * Math.Max(1, LootTables.Get(table).Pools.Max(p => p.Rolls.Max)));
        var drops = world.FloorDrops.Snapshot();
        if (allowed.Count == 0) Assert.Empty(drops);
        foreach (var drop in drops)
        {
            Assert.True(drop.Id.IsItem);
            Assert.True(allowed.TryGetValue(drop.Id.Value, out var max), $"floor holds item {drop.Id.Value}, which '{table}' never awards");
            Assert.InRange(drop.Count, 1, max);
        }
    }
}

using Xunit;
using Zenith.Gameplay.Survival;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 13: a break rolls its drops (count ranges, "instead", bonus) and digging slows in the air and under water (item 7).</summary>
public sealed class BlockLootRollTests
{
    public BlockLootRollTests()
    {
        Blocks.EnsureLoaded();
        BlockLoot.EnsureLoaded();
    }

    private static int Id(string name) => Blocks.Palette.Require(name);

    private static List<BlockDrop> Break(string block, int times, int seed = 3)
    {
        var random = new Random(seed);
        var all = new List<BlockDrop>();
        for (var i = 0; i < times; i++) BlockLoot.Roll(Id(block), random, all);
        return all;
    }

    [Fact]
    public void Redstone_ore_drops_four_or_five_and_lapis_four_to_nine()
    {
        var redstone = Break("minecraft:redstone_ore", 200);
        Assert.All(redstone, drop => Assert.InRange(drop.Count, 4, 5));
        Assert.Contains(redstone, drop => drop.Count == 4);
        Assert.Contains(redstone, drop => drop.Count == 5);

        var lapis = Break("minecraft:lapis_ore", 300);
        Assert.All(lapis, drop => Assert.InRange(drop.Count, 4, 9));
        Assert.Equal(6, lapis.Select(drop => drop.Count).Distinct().Count());
    }

    [Fact]
    public void Iron_and_gold_ore_drop_the_raw_item_not_the_block()
    {
        foreach (var ore in new[] { "minecraft:iron_ore", "minecraft:gold_ore" })
        {
            var drop = Assert.Single(Break(ore, 1));
            Assert.True(drop.Id.IsItem);
        }
    }

    [Fact]
    public void Gravel_gives_flint_about_one_time_in_ten_instead_of_itself()
    {
        var drops = Break("minecraft:gravel", 2000);
        Assert.Equal(2000, drops.Count); // flint replaces the gravel, never joins it
        var flint = drops.Count(drop => drop.Id.IsItem);
        Assert.InRange(flint, 140, 260);
    }

    [Fact]
    public void Oak_leaves_drop_nothing_most_of_the_time_and_sometimes_a_sapling_a_stick_or_an_apple()
    {
        var drops = Break("minecraft:oak_leaves", 4000);
        Assert.All(drops, drop => Assert.True(drop.Id.IsItem)); // the leaves themselves never drop
        Assert.InRange(drops.Count, 200, 600); // 5% sapling + 2% sticks + 0.5% apples of 4000
    }

    [Fact]
    public void Tall_grass_gives_seeds_sometimes()
    {
        var seeds = Break("minecraft:short_grass", 2000);
        Assert.InRange(seeds.Count, 180, 330);
    }

    [Fact]
    public void A_plain_block_still_drops_exactly_itself()
    {
        var drop = Assert.Single(Break("minecraft:oak_planks", 1));
        Assert.Equal(1, drop.Count);
    }

    [Fact]
    public void Digging_takes_five_times_as_long_in_the_air_and_the_penalties_stack()
    {
        var stone = Id("minecraft:stone");
        var grounded = Blocks.BreakTicks(stone, default);
        var airborne = Blocks.BreakTicks(stone, default, DigPenalty.Factor);
        var both = Blocks.BreakTicks(stone, default, DigPenalty.Factor * DigPenalty.Factor);
        Assert.InRange(airborne, grounded * 5 - 4, grounded * 5);
        Assert.InRange(both, grounded * 25 - 24, grounded * 25);
    }
}

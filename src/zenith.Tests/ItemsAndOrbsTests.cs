using Xunit;
using Zenith.Gameplay.WorldInteraction;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 items 15 and 16: piles and orbs fold together, items float, burn and fall out of the world, ores give experience.</summary>
public sealed class ItemsAndOrbsTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public ItemsAndOrbsTests() => Blocks.EnsureLoaded();

    private static async Task<IntentTestFixture> Stage()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 6; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        return fx;
    }

    private static StackId Item(IntentTestFixture fx, string name = "minecraft:string") => StackId.FromItem(fx.Context.ItemPalette.Require(name));

    private static void Drop(IntentTestFixture fx, int x, int y, int z, StackId id, int count, long entity) =>
        Assert.True(fx.World.FloorDrops.TryAddOrMerge(x, y, z, id, count, entity, out _, pickupDelayTicks: 1000));

    private static void TickAt(IntentTestFixture fx, FloorDropSystem system, ulong tick)
    {
        fx.Clock.AdvanceBy((int)(tick - fx.Clock.CurrentTick));
        system.Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public async Task Two_settled_piles_of_the_same_item_next_to_each_other_become_one()
    {
        var fx = await Stage();
        Drop(fx, 8, Level, 8, Item(fx), 5, 101);
        Drop(fx, 9, Level, 8, Item(fx), 7, 102);

        TickAt(fx, new FloorDropSystem(fx.World), 40);

        var pile = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(12, pile.Count);
    }

    [Fact]
    public async Task Different_items_and_piles_that_would_overflow_a_stack_stay_apart()
    {
        var fx = await Stage();
        Drop(fx, 8, Level, 8, Item(fx), 5, 101);
        Drop(fx, 9, Level, 8, Item(fx, "minecraft:stick"), 7, 102);
        Drop(fx, 8, Level, 9, Item(fx), 64, 103);

        TickAt(fx, new FloorDropSystem(fx.World), 40);

        Assert.Equal(3, fx.World.FloorDrops.Count);
    }

    [Fact]
    public async Task A_pile_far_from_the_other_is_left_alone()
    {
        var fx = await Stage();
        Drop(fx, 2, Level, 2, Item(fx), 5, 101);
        Drop(fx, 12, Level, 12, Item(fx), 5, 102);

        TickAt(fx, new FloorDropSystem(fx.World), 40);

        Assert.Equal(2, fx.World.FloorDrops.Count);
    }

    [Fact]
    public async Task A_pile_that_lands_in_lava_burns_up_and_one_in_the_void_is_lost()
    {
        var fx = await Stage();
        fx.World.SetBlock(8, Level, 8, Blocks.Lava);
        Drop(fx, 8, Level, 8, Item(fx), 5, 101);
        Drop(fx, 3, Level, 3, Item(fx), 5, 102);
        Drop(fx, 5, Blocks.FlatMinY - 40, 5, Item(fx), 5, 103);

        TickAt(fx, new FloorDropSystem(fx.World), 40);

        var left = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(102, left.EntityRuntimeId);
    }

    [Fact]
    public async Task An_item_in_water_rises_where_one_in_air_falls()
    {
        var fx = await Stage();
        for (var y = Level; y < Level + 4; y++) fx.World.SetBlock(8, y, 8, Blocks.Water);

        var inWater = new FloorDropMotion(8.5f, Level + 0.5f, 8.5f, 0, 0, 0);
        var inAir = new FloorDropMotion(3.5f, Level + 3f, 3.5f, 0, 0, 0);
        for (var i = 0; i < 10; i++)
        {
            inWater = FloorDropPhysics.Step(fx.World, inWater);
            inAir = FloorDropPhysics.Step(fx.World, inAir);
        }

        Assert.True(inWater.Y > Level + 0.5f, $"water {inWater.Y}");
        Assert.True(inAir.Y < Level + 3f, $"air {inAir.Y}");

        var orb = FloorDropPhysics.Step(fx.World, new FloorDropMotion(8.5f, Level + 0.5f, 8.5f, 0, 0, 0), buoyant: false);
        Assert.True(orb.VelocityY < 0);
    }

    [Fact]
    public async Task Resting_orbs_next_to_each_other_become_one_with_both_values()
    {
        var fx = await Stage();
        ExperienceOrbEntry Orb(float x, int value) => new()
        {
            EntityId = fx.Players.AllocateRuntimeId(), Value = value, Pose = new FloorDropMotion(x, Level, 8.5f, 0, 0, 0)
        };
        Assert.True(fx.World.ExperienceOrbs.TrySpawn(Orb(8.5f, 3)));
        Assert.True(fx.World.ExperienceOrbs.TrySpawn(Orb(8.7f, 5)));
        Assert.True(fx.World.ExperienceOrbs.TrySpawn(Orb(13.5f, 7))); // too far to join

        fx.Clock.AdvanceBy(10);
        new ExperienceOrbSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, fx.World.ExperienceOrbs.Count);
        Assert.Equal(new[] { 7, 8 }, fx.World.ExperienceOrbs.Active.Select(o => o.Value).Order().ToArray());
    }

    [Fact]
    public void Ores_give_experience_in_their_ranges_and_stone_gives_none()
    {
        var random = new Random(4);
        var seen = new HashSet<int>();
        for (var i = 0; i < 200; i++)
        {
            Assert.True(BlockExperience.TryRoll(Blocks.DiamondOre, random, out var points));
            Assert.InRange(points, 3, 7);
            seen.Add(points);
        }

        Assert.Equal(5, seen.Count);
        for (var i = 0; i < 50; i++)
            if (BlockExperience.TryRoll(Blocks.CoalOre, random, out var coal)) Assert.InRange(coal, 1, 2);
        Assert.False(BlockExperience.TryRoll(Blocks.Stone, random, out _));
    }
}

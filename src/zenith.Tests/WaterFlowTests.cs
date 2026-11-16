using Xunit;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.Fluids;
using Zenith.World;

namespace Zenith.Tests;

public sealed class WaterFlowTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public WaterFlowTests() => Blocks.EnsureLoaded();

    private static int WaterOf(int depth)
    {
        var id = Blocks.Water;
        Assert.True(Blocks.Catalog.States.TrySet(ref id, StateKeys.LiquidDepth, depth));
        return id;
    }

    private static int DepthAt(IntentTestFixture fx, int x, int y, int z)
    {
        var id = fx.World.GetBlock(x, y, z);
        return Blocks.IsFluid(id) && Blocks.Catalog.States.TryGet(id, StateKeys.LiquidDepth, out var depth) ? depth : -1;
    }

    /// <summary>A stone floor over one chunk with air above it, a viewer so the chunk stays resident.</summary>
    private static async Task<IntentTestFixture> FixtureWithFloor()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                for (var y = Floor - 3; y <= Floor; y++) fx.World.SetBlock(x, y, z, Blocks.Stone);
                for (var y = Level; y < Level + 4; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }
        return fx;
    }

    private static void Run(IntentTestFixture fx, int ticks)
    {
        var system = new BlockUpdateSystem(fx.World, fx.Players, fx.Context.EnvironmentPolicies);
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
    }

    [Fact]
    public void Every_level_of_water_counts_as_fluid_and_stone_does_not()
    {
        for (var depth = 0; depth < 16; depth++) Assert.True(Blocks.IsFluid(WaterOf(depth)));
        Assert.False(Blocks.IsFluid(Blocks.Stone));
        Assert.Equal(WaterOf(0), Blocks.Water);
    }

    [Fact]
    public async Task A_source_on_the_ground_spreads_seven_blocks_getting_thinner()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, WaterOf(0));

        Run(fx, 120);

        Assert.Equal(0, DepthAt(fx, 8, Level, 8));
        for (var reach = 1; reach <= 7; reach++)
        {
            Assert.Equal(reach, DepthAt(fx, 8 + reach, Level, 8));
            Assert.Equal(reach, DepthAt(fx, 8 - reach, Level, 8));
            Assert.Equal(reach, DepthAt(fx, 8, Level, 8 + reach));
        }
    }

    [Fact]
    public async Task Water_falls_down_a_shaft_and_keeps_spreading_on_top()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, WaterOf(0));
        fx.World.SetBlock(8, Floor, 8, Blocks.Air);
        fx.World.SetBlock(8, Floor - 1, 8, Blocks.Air);
        fx.World.SetBlock(8, Floor - 2, 8, Blocks.Stone);

        Run(fx, 120);

        Assert.Equal(FluidFlow.Falling, DepthAt(fx, 8, Floor, 8));
        Assert.Equal(FluidFlow.Falling, DepthAt(fx, 8, Floor - 1, 8));
        Assert.Equal(1, DepthAt(fx, 9, Level, 8)); // the source keeps spreading across the top while it drains down
        Assert.Equal(-1, DepthAt(fx, 9, Floor - 1, 8)); // the shaft's walls are stone
    }

    [Fact]
    public async Task A_flow_dries_up_when_its_source_is_taken_away()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, WaterOf(0));
        Run(fx, 120);
        Assert.Equal(3, DepthAt(fx, 11, Level, 8));

        Assert.True(fx.World.TrySetBlock(8, Level, 8, Blocks.Air));
        Run(fx, 300);

        for (var x = 1; x <= 15; x++) Assert.Equal(-1, DepthAt(fx, x, Level, 8));
    }

    [Fact]
    public async Task Two_sources_with_a_gap_between_them_make_the_gap_a_source()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(6, Level, 8, WaterOf(0));
        fx.World.SetBlock(8, Level, 8, WaterOf(0));

        Run(fx, 120);

        Assert.Equal(0, DepthAt(fx, 7, Level, 8));
    }

    [Fact]
    public async Task Water_stops_at_the_edge_of_the_loaded_world_without_loading_anything()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(1, Level, 8, WaterOf(0));
        var loads = fx.World.SyncLoadCount;

        Run(fx, 120);

        Assert.Equal(1, DepthAt(fx, 0, Level, 8));
        Assert.Equal(loads, fx.World.SyncLoadCount);
    }

    [Fact]
    public async Task Water_runs_towards_a_drop_off_within_four_blocks_instead_of_everywhere()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(11, Floor, 8, Blocks.Air); // a pit three blocks east of the source
        fx.World.SetBlock(8, Level, 8, WaterOf(0));

        Run(fx, 12);

        Assert.Equal(1, DepthAt(fx, 9, Level, 8));
        Assert.Equal(-1, DepthAt(fx, 7, Level, 8));
        Assert.Equal(-1, DepthAt(fx, 8, Level, 9));
    }

    [Fact]
    public async Task Water_with_no_drop_off_in_reach_spreads_every_way()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(14, Floor, 8, Blocks.Air); // six blocks away: out of reach
        fx.World.SetBlock(8, Level, 8, WaterOf(0));

        Run(fx, 12);

        foreach (var (x, z) in new[] { (9, 8), (7, 8), (8, 9), (8, 7) }) Assert.Equal(1, DepthAt(fx, x, Level, z));
    }

    [Fact]
    public async Task Water_washes_a_plant_away()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(9, Level, 8, Blocks.Dandelion);
        fx.World.SetBlock(8, Level, 8, WaterOf(0));

        Run(fx, 12);

        Assert.Equal(1, DepthAt(fx, 9, Level, 8));
    }

    [Fact]
    public async Task A_source_is_not_overwritten_by_water_flowing_into_it()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, WaterOf(0));
        fx.World.SetBlock(10, Level, 8, WaterOf(0));

        Run(fx, 120);

        Assert.Equal(0, DepthAt(fx, 8, Level, 8));
        Assert.Equal(0, DepthAt(fx, 10, Level, 8));
    }

    [Fact]
    public async Task A_door_holds_water_back_instead_of_being_washed_away()
    {
        var fx = await FixtureWithFloor();
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:wooden_door", out var door));
        fx.World.SetBlock(9, Level, 8, door);
        fx.World.SetBlock(8, Level, 8, WaterOf(0));

        Run(fx, 30);

        Assert.Equal(door, fx.World.GetBlock(9, Level, 8));
    }
}

public sealed class LavaFlowTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public LavaFlowTests() => Blocks.EnsureLoaded();

    private static int LiquidAt(FluidType type, int depth)
    {
        var id = type == FluidType.Lava ? Blocks.Lava : Blocks.Water;
        Assert.True(Blocks.Catalog.States.TrySet(ref id, StateKeys.LiquidDepth, depth));
        return id;
    }

    private static int DepthAt(IntentTestFixture fx, FluidType type, int x, int y, int z)
    {
        var id = fx.World.GetBlock(x, y, z);
        return Blocks.FluidOf(id) == type && Blocks.Catalog.States.TryGet(id, StateKeys.LiquidDepth, out var depth) ? depth : -1;
    }

    private static async Task<IntentTestFixture> FixtureWithFloor()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                for (var y = Floor - 3; y <= Floor; y++) fx.World.SetBlock(x, y, z, Blocks.Stone);
                for (var y = Level; y < Level + 4; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }
        return fx;
    }

    private static void Run(IntentTestFixture fx, int ticks)
    {
        var system = new BlockUpdateSystem(fx.World, fx.Players, fx.Context.EnvironmentPolicies);
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
    }

    [Fact]
    public void Lava_and_water_are_told_apart_at_every_depth()
    {
        for (var depth = 0; depth < 16; depth++)
        {
            Assert.Equal(FluidType.Lava, Blocks.FluidOf(LiquidAt(FluidType.Lava, depth)));
            Assert.Equal(FluidType.Water, Blocks.FluidOf(LiquidAt(FluidType.Water, depth)));
        }

        Assert.Equal(FluidType.None, Blocks.FluidOf(Blocks.Stone));
    }

    [Fact]
    public async Task Lava_spreads_three_blocks_in_steps_of_two_and_slowly()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, LiquidAt(FluidType.Lava, 0));

        Run(fx, 20);
        Assert.Equal(-1, DepthAt(fx, FluidType.Lava, 9, Level, 8)); // not yet: lava waits thirty ticks

        Run(fx, 400);
        Assert.Equal(2, DepthAt(fx, FluidType.Lava, 9, Level, 8));
        Assert.Equal(4, DepthAt(fx, FluidType.Lava, 10, Level, 8));
        Assert.Equal(6, DepthAt(fx, FluidType.Lava, 11, Level, 8));
        Assert.Equal(-1, DepthAt(fx, FluidType.Lava, 12, Level, 8));
    }

    [Fact]
    public async Task Lava_never_makes_a_new_source_between_two_sources()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(6, Level, 8, LiquidAt(FluidType.Lava, 0));
        fx.World.SetBlock(8, Level, 8, LiquidAt(FluidType.Lava, 0));

        Run(fx, 400);

        Assert.NotEqual(0, DepthAt(fx, FluidType.Lava, 7, Level, 8));
    }

    [Fact]
    public async Task Water_beside_a_lava_source_makes_obsidian()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, LiquidAt(FluidType.Lava, 0));
        fx.World.SetBlock(9, Level, 8, LiquidAt(FluidType.Water, 0));

        Run(fx, 5);

        Assert.Equal(Blocks.Obsidian, fx.World.GetBlock(8, Level, 8));
    }

    [Fact]
    public async Task Water_beside_flowing_lava_makes_cobblestone()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, LiquidAt(FluidType.Lava, 2));
        fx.World.SetBlock(9, Level, 8, LiquidAt(FluidType.Water, 0));

        Run(fx, 5);

        Assert.Equal(Blocks.Cobblestone, fx.World.GetBlock(8, Level, 8));
    }

    [Fact]
    public async Task Water_above_lava_makes_it_set_too()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, LiquidAt(FluidType.Lava, 0));
        fx.World.SetBlock(8, Level + 1, 8, LiquidAt(FluidType.Water, 0));

        Run(fx, 5);

        Assert.Equal(Blocks.Obsidian, fx.World.GetBlock(8, Level, 8));
    }

    [Fact]
    public async Task Lava_falling_onto_water_turns_the_water_to_stone()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, LiquidAt(FluidType.Water, 0));
        fx.World.SetBlock(8, Level + 1, 8, LiquidAt(FluidType.Lava, 0));
        // the lava touches the water below it, which only counts sideways and from above: the water is below, so it falls
        Run(fx, 100);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(8, Level, 8));
    }

    [Fact]
    public async Task A_lone_lava_source_stays_a_source()
    {
        var fx = await FixtureWithFloor();
        fx.World.SetBlock(8, Level, 8, LiquidAt(FluidType.Lava, 0));

        Run(fx, 100);

        Assert.Equal(0, DepthAt(fx, FluidType.Lava, 8, Level, 8));
    }
}

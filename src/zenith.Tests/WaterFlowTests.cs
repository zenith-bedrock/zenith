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
        var system = new BlockUpdateSystem(fx.World, fx.Players);
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

        Assert.Equal(WaterFlow.Falling, DepthAt(fx, 8, Floor, 8));
        Assert.Equal(WaterFlow.Falling, DepthAt(fx, 8, Floor - 1, 8));
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
}

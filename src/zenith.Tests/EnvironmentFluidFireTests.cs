using Xunit;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.Fire;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.Modules.Protect;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Plan 002, phase 2: liquids, fire and lava ignition ask the environment policies per cell. A vetoed cell behaves like a wall: not
/// entered, not burned, not washed away, nothing dropped, nothing left queued for it. Every refusal has a control run without the
/// region, so a test cannot pass because the scenario never did anything.
/// </summary>
public sealed class EnvironmentFluidFireTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public EnvironmentFluidFireTests() => Blocks.EnsureLoaded();

    private static async Task<IntentTestFixture> Ground()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                for (var y = Floor - 3; y <= Floor; y++) fx.World.SetBlock(x, y, z, Blocks.Stone);
                for (var y = Level; y < Level + 6; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        fx.World.BlockUpdates.Clear();
        return fx;
    }

    private static void Protect(IntentTestFixture fx, int x1, int z1, int x2, int z2) =>
        Assert.True(fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("vault", x1, Level - 2, z1, x2, Level + 5, z2), out _));

    private static BlockUpdateSystem Run(IntentTestFixture fx, int ticks, BlockUpdateSystem? system = null)
    {
        system ??= new BlockUpdateSystem(fx.World, fx.Players, fx.Context.EnvironmentPolicies);
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }

        return system;
    }

    private static int WaterOf(int depth)
    {
        var id = Blocks.Water;
        Assert.True(Blocks.Catalog.States.TrySet(ref id, StateKeys.LiquidDepth, depth));
        return id;
    }

    private static int Torch()
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:torch", out var id));
        return id;
    }

    // ---- liquids ----

    [Fact]
    public async Task Water_does_not_enter_a_region_and_stops_at_its_edge()
    {
        var control = await Ground();
        control.World.SetBlock(5, Level, 8, WaterOf(0));
        Run(control, 150);
        Assert.True(Blocks.IsFluid(control.World.GetBlock(10, Level, 8)), "control: water reaches x=10 without a region");

        var fx = await Ground();
        Protect(fx, 10, 0, 15, 15);
        fx.World.SetBlock(5, Level, 8, WaterOf(0));
        Run(fx, 150);

        Assert.True(Blocks.IsFluid(fx.World.GetBlock(9, Level, 8)), "it still flows right up to the edge");
        for (var x = 10; x <= 15; x++)
            for (var z = 0; z <= 15; z++)
                Assert.False(Blocks.IsFluid(fx.World.GetBlock(x, Level, z)), $"{x},{z}");
    }

    [Fact]
    public async Task A_torch_in_a_region_is_not_washed_away_and_nothing_is_dropped()
    {
        var control = await Ground();
        control.World.SetBlock(9, Level, 8, Torch());
        control.World.SetBlock(8, Level, 8, WaterOf(0));
        Run(control, 60);
        Assert.NotEqual(Torch(), control.World.GetBlock(9, Level, 8));
        Assert.True(control.World.FloorDrops.Count > 0, "control: washing the torch away drops it");

        var fx = await Ground();
        Protect(fx, 9, 0, 15, 15);
        fx.World.SetBlock(9, Level, 8, Torch());
        fx.World.SetBlock(8, Level, 8, WaterOf(0));
        Run(fx, 60);

        Assert.Equal(Torch(), fx.World.GetBlock(9, Level, 8));
        Assert.Equal(0, fx.World.FloorDrops.Count);
    }

    [Fact]
    public async Task Nothing_is_left_queued_for_a_vetoed_cell_and_the_world_goes_quiet()
    {
        var fx = await Ground();
        Protect(fx, 10, 0, 15, 15);
        fx.World.SetBlock(5, Level, 8, WaterOf(0));
        var system = Run(fx, 200);

        // Quiet means: further ticks change nothing and queue nothing.
        Assert.Equal(0, fx.World.ScheduledTicks.Count);
        Assert.Equal(0, fx.World.BlockUpdates.Count);
        var before = Snapshot(fx);
        Run(fx, 100, system);
        Assert.Equal(before, Snapshot(fx));
        Assert.Equal(0, fx.World.ScheduledTicks.Count);
    }

    private static string Snapshot(IntentTestFixture fx)
    {
        var cells = new System.Text.StringBuilder();
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
                cells.Append(fx.World.GetBlock(x, Level, z)).Append(',');
        return cells.ToString();
    }

    [Fact]
    public async Task After_the_region_is_removed_water_resumes_on_the_next_neighbour_update_and_not_before()
    {
        var fx = await Ground();
        Protect(fx, 10, 0, 15, 15);
        fx.World.SetBlock(5, Level, 8, WaterOf(0));
        var system = Run(fx, 150);
        Assert.False(Blocks.IsFluid(fx.World.GetBlock(10, Level, 8)));

        Assert.True(fx.Context.Protect.Regions.TryRemove("vault"));
        Run(fx, 150, system);
        Assert.False(Blocks.IsFluid(fx.World.GetBlock(10, Level, 8)), "lifting the protection replays nothing by itself");

        // Something touches the edge cell: the liquid there is re-evaluated and flows on.
        _ = fx.World.BlockUpdates.Enqueue(9, Level, 8);
        Run(fx, 150, system);
        Assert.True(Blocks.IsFluid(fx.World.GetBlock(10, Level, 8)));
    }

    [Fact]
    public async Task A_flowing_cell_in_a_region_is_not_dried_up()
    {
        var control = await Ground();
        control.World.SetBlock(8, Level, 8, WaterOf(3)); // nothing feeds it
        _ = control.World.BlockUpdates.Enqueue(8, Level, 8);
        Run(control, 60);
        Assert.False(Blocks.IsFluid(control.World.GetBlock(8, Level, 8)), "control: unfed flowing water dries up");

        var fx = await Ground();
        Protect(fx, 8, 8, 8, 8);
        fx.World.SetBlock(8, Level, 8, WaterOf(3));
        _ = fx.World.BlockUpdates.Enqueue(8, Level, 8);
        Run(fx, 60);

        Assert.Equal(WaterOf(3), fx.World.GetBlock(8, Level, 8));
    }

    [Fact]
    public async Task Protected_lava_beside_water_does_not_turn_to_obsidian()
    {
        var control = await Ground();
        control.World.SetBlock(10, Level, 8, Blocks.Lava);
        control.World.SetBlock(9, Level, 8, WaterOf(0));
        Run(control, 10);
        Assert.Equal(Blocks.Obsidian, control.World.GetBlock(10, Level, 8));

        var fx = await Ground();
        Protect(fx, 10, 8, 10, 8);
        fx.World.SetBlock(10, Level, 8, Blocks.Lava);
        fx.World.SetBlock(9, Level, 8, WaterOf(0));
        Run(fx, 60);

        Assert.Equal(Blocks.Lava, fx.World.GetBlock(10, Level, 8));
    }

    // ---- fire ----

    [Fact]
    public async Task Fire_does_not_eat_planks_in_a_region_nor_light_the_air_above_them()
    {
        static void Build(IntentTestFixture fx)
        {
            for (var x = 5; x <= 8; x++)
                for (var y = Level; y < Level + 2; y++) fx.World.SetBlock(x, y, 5, Blocks.OakPlanks);
            fx.World.SetBlock(4, Level, 5, FireBlocks.WithAge(0));
        }

        var control = await Ground();
        Build(control);
        Run(control, 4000);
        Assert.True(Blocks.IsAir(control.World.GetBlock(5, Level, 5)) || Blocks.IsFire(control.World.GetBlock(5, Level, 5)),
            "control: fire consumes the planks");

        var fx = await Ground();
        Build(fx);
        Protect(fx, 5, 0, 15, 15);
        Run(fx, 4000);

        for (var x = 5; x <= 8; x++)
            for (var y = Level; y < Level + 6; y++)
            {
                var block = fx.World.GetBlock(x, y, 5);
                var expected = y < Level + 2 ? Blocks.OakPlanks : Blocks.Air;
                Assert.True(block == expected, $"{x},{y}: expected {expected}, found {block}");
            }
    }

    [Fact]
    public async Task A_fire_inside_a_region_is_left_as_it_is_and_nothing_is_scheduled_for_it()
    {
        var control = await Ground();
        control.World.SetBlock(5, Level, 5, FireBlocks.WithAge(0));
        Run(control, 4000);
        Assert.True(Blocks.IsAir(control.World.GetBlock(5, Level, 5)), "control: bare fire goes out");

        var fx = await Ground();
        Protect(fx, 5, 5, 5, 5);
        fx.World.SetBlock(5, Level, 5, FireBlocks.WithAge(0));
        var system = Run(fx, 4000);

        Assert.True(Blocks.IsFire(fx.World.GetBlock(5, Level, 5)));
        Assert.Equal(0, FireBlocks.AgeOf(fx.World.GetBlock(5, Level, 5)));
        Assert.Equal(0, fx.World.ScheduledTicks.Count);
        _ = system;
    }

    [Fact]
    public async Task Lava_does_not_light_fire_in_a_region()
    {
        static bool LitSomewhere(IntentTestFixture fx)
        {
            var lit = false;
            for (var x = 4; x <= 7; x++)
                for (var y = Level; y <= Level + 2; y++)
                    for (var z = 4; z <= 6; z++)
                        lit |= Blocks.IsFire(fx.World.GetBlock(x, y, z));
            return lit;
        }

        void Ignite(IntentTestFixture fx)
        {
            var tick = new RandomTickContext(fx.World, new Random(7), [], fx.Context.EnvironmentPolicies);
            var behavior = new LavaIgnitionBehavior();
            for (var i = 0; i < 400; i++)
            {
                tick.Aim(5, Level, 5, Blocks.Lava);
                behavior.OnRandomTick(tick);
            }
        }

        var control = await Ground();
        control.World.SetBlock(5, Level, 5, Blocks.Lava);
        control.World.SetBlock(6, Level, 5, Blocks.OakPlanks);
        Ignite(control);
        Assert.True(LitSomewhere(control), "control: lava lights the air beside planks");

        var fx = await Ground();
        fx.World.SetBlock(5, Level, 5, Blocks.Lava);
        fx.World.SetBlock(6, Level, 5, Blocks.OakPlanks);
        Protect(fx, 0, 0, 15, 15); // everything around the lava is protected, the lava cell included
        Ignite(fx);

        Assert.False(LitSomewhere(fx));
    }
}

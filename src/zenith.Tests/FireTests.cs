using Xunit;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.Fire;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Fire and lava ignition (ADR §172).</summary>
public sealed class FireTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public FireTests() => Blocks.EnsureLoaded();

    private static async Task<IntentTestFixture> Ground()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 6; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        fx.World.BlockUpdates.Clear();
        return fx;
    }

    private static void Run(IntentTestFixture fx, BlockUpdateSystem system, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
    }

    [Fact]
    public async Task Fire_with_nothing_to_burn_goes_out_on_its_own()
    {
        var fx = await Ground();
        var system = new BlockUpdateSystem(fx.World, fx.Players);
        fx.World.SetBlock(5, Level, 5, FireBlocks.WithAge(0));

        Run(fx, system, 4000); // ~110 steps: it has aged past 3 many times over

        Assert.True(Blocks.IsAir(fx.World.GetBlock(5, Level, 5)));
    }

    [Fact]
    public async Task Fire_next_to_planks_spreads_and_eats_them()
    {
        var fx = await Ground();
        var system = new BlockUpdateSystem(fx.World, fx.Players);
        for (var x = 4; x <= 8; x++)
            for (var y = Level; y < Level + 3; y++) fx.World.SetBlock(x, y, 5, Blocks.OakPlanks);
        fx.World.SetBlock(4, Level, 4, FireBlocks.WithAge(0));

        Run(fx, system, 2000);

        var planks = 0;
        for (var x = 4; x <= 8; x++)
            for (var y = Level; y < Level + 3; y++)
                if (fx.World.GetBlock(x, y, 5) == Blocks.OakPlanks) planks++;
        Assert.True(planks < 15, $"{planks} of 15 planks left");
    }

    [Fact]
    public async Task Water_beside_a_fire_puts_it_out()
    {
        var fx = await Ground();
        var system = new BlockUpdateSystem(fx.World, fx.Players);
        fx.World.SetBlock(5, Level - 0, 6, Blocks.OakPlanks);
        fx.World.SetBlock(5, Level, 5, FireBlocks.WithAge(0));
        fx.World.SetBlock(6, Level, 5, Blocks.Water);

        Run(fx, system, 80);

        var left = fx.World.GetBlock(5, Level, 5);
        Assert.False(Blocks.IsFire(left)); // out, and the water has since run into the cell
    }

    [Fact]
    public async Task Lava_lights_the_air_over_a_flammable_neighbour()
    {
        var fx = await Ground();
        var tick = new RandomTickContext(fx.World, new Random(7), []);
        fx.World.SetBlock(5, Level, 5, Blocks.Lava);
        fx.World.SetBlock(6, Level, 5, Blocks.OakPlanks);
        var behavior = new LavaIgnitionBehavior();

        for (var i = 0; i < 200; i++)
        {
            tick.Aim(5, Level, 5, Blocks.Lava);
            behavior.OnRandomTick(tick);
        }

        var lit = false;
        for (var x = 4; x <= 7; x++)
            for (var y = Level; y <= Level + 2; y++)
                for (var z = 4; z <= 6; z++)
                    lit |= Blocks.IsFire(fx.World.GetBlock(x, y, z));
        Assert.True(lit);
    }

    [Fact]
    public void Lava_and_fire_carry_the_data_they_run_on()
    {
        Assert.True(Blocks.IsRandomTicking(Blocks.Lava));
        Assert.True(Blocks.FlammabilityOf(Blocks.OakPlanks) > 0);
        Assert.True(Blocks.FlameEncouragementOf(Blocks.OakPlanks) > 0);
        Assert.Equal(0, Blocks.FlammabilityOf(Blocks.Stone));
        Assert.Equal(7, FireBlocks.AgeOf(FireBlocks.WithAge(7)));
        Assert.False(Blocks.BlocksMovement(Blocks.Fire));
    }
}

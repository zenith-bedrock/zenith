using Xunit;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.Modules.Protect;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Plan 002, phase 5: random ticks (growth, decay, spread) ask the environment policies, each under its own cause. A tree is one
/// planned unit: a refusal anywhere in its footprint means no trunk and no canopy, never half of one. Each refusal has a control run
/// without the region.
/// </summary>
public sealed class EnvironmentRandomTickTests
{
    public EnvironmentRandomTickTests() => Blocks.EnsureLoaded();

    private static async Task<IntentTestFixture> Viewed()
    {
        var fx = new IntentTestFixture();
        Assert.True(fx.World.Rules.TrySet(Rulebook.GameRules.RandomTickSpeedName, "4096", out _));
        fx.AddInGamePlayer("ticker");
        await fx.World.GetOrCreateColumnAsync(0, 0);
        fx.World.ChunkResidency.Acquire(0, 0);
        return fx;
    }

    private static void Protect(IntentTestFixture fx, int x1, int y1, int z1, int x2, int y2, int z2) =>
        Assert.True(fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("vault", x1, y1, z1, x2, y2, z2), out _));

    private static void Run(IntentTestFixture fx, int ticks)
    {
        var system = new RandomTickSystem(fx.World, RandomTickBehaviors.Default(), fx.Context.EnvironmentPolicies, new Random(1));
        var clock = new GameClock();
        for (var i = 0; i < ticks; i++)
        {
            clock.AdvanceBy(1);
            system.Tick(clock, fx.Players.Online);
        }
    }

    private static async Task<IntentTestFixture> WithSapling()
    {
        var fx = await Viewed();
        fx.World.SetBlock(8, 63, 8, Blocks.Dirt);
        fx.World.SetBlock(8, 64, 8, Blocks.OakSapling);
        return fx;
    }

    private static (int Logs, int Leaves) CountTree(IntentTestFixture fx)
    {
        int logs = 0, leaves = 0;
        for (var x = 4; x <= 12; x++)
            for (var z = 4; z <= 12; z++)
                for (var y = 64; y <= 72; y++)
                {
                    var id = fx.World.GetBlock(x, y, z);
                    if (Blocks.IsLog(id)) logs++;
                    if (Blocks.IsLeaves(id)) leaves++;
                }

        return (logs, leaves);
    }

    [Fact]
    public async Task One_protected_canopy_cell_means_no_tree_at_all()
    {
        var control = await WithSapling();
        Run(control, 1500);
        var grown = CountTree(control);
        Assert.True(grown.Logs >= 4 && grown.Leaves > 0, "control: the sapling grows a tree");

        var fx = await WithSapling();
        Protect(fx, 9, 67, 8, 9, 67, 8); // a leaf cell every trunk height needs
        Run(fx, 1500);

        Assert.Equal((0, 0), CountTree(fx));
        Assert.False(Blocks.IsLog(fx.World.GetBlock(8, 64, 8)));
    }

    [Fact]
    public async Task A_protected_trunk_cell_means_no_tree_at_all()
    {
        var fx = await WithSapling();
        Protect(fx, 8, 66, 8, 8, 66, 8);
        Run(fx, 1500);

        Assert.Equal((0, 0), CountTree(fx));
    }

    [Fact]
    public async Task A_sapling_in_a_region_does_not_even_mature()
    {
        var fx = await WithSapling();
        var before = fx.World.GetBlock(8, 64, 8);
        Protect(fx, 8, 64, 8, 8, 64, 8);
        Run(fx, 1500);

        Assert.Equal(before, fx.World.GetBlock(8, 64, 8));
    }

    [Fact]
    public async Task A_region_elsewhere_does_not_stop_a_tree()
    {
        var fx = await WithSapling();
        Protect(fx, 0, 60, 0, 2, 80, 2);
        Run(fx, 1500);

        Assert.True(CountTree(fx).Logs >= 4);
    }

    [Fact]
    public async Task Leaves_inside_a_region_do_not_decay()
    {
        var control = await Viewed();
        control.World.SetBlock(5, 70, 5, Blocks.OakLeaves);
        Run(control, 100);
        Assert.True(Blocks.IsAir(control.World.GetBlock(5, 70, 5)), "control: a lone leaf decays");

        var fx = await Viewed();
        fx.World.SetBlock(5, 70, 5, Blocks.OakLeaves);
        Protect(fx, 5, 70, 5, 5, 70, 5);
        Run(fx, 300);

        Assert.Equal(Blocks.OakLeaves, fx.World.GetBlock(5, 70, 5));
    }

    [Fact]
    public async Task Grass_does_not_spread_into_a_region()
    {
        var control = await Viewed();
        control.World.SetBlock(5, 64, 5, Blocks.GrassBlock);
        control.World.SetBlock(6, 64, 5, Blocks.Dirt);
        Run(control, 100);
        Assert.Equal(Blocks.GrassBlock, control.World.GetBlock(6, 64, 5));

        var fx = await Viewed();
        fx.World.SetBlock(5, 64, 5, Blocks.GrassBlock);
        fx.World.SetBlock(6, 64, 5, Blocks.Dirt);
        Protect(fx, 6, 64, 5, 6, 64, 5);
        Run(fx, 300);

        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(6, 64, 5));
    }
}

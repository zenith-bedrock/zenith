using Zenith.Gameplay.WorldInteraction.Fire;
using Xunit;
using Zenith.Gameplay.Policy;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Plan 003, the old gap: whatever removes the block under a sand or gravel block (an explosion, decaying leaves, burning wood, a
/// plain world write) makes gravity notice it, not only a player's edit. The noticing sits in the world write, so no writer can forget it.
/// </summary>
public sealed class GravityNoticeTests
{
    private static readonly int Ground = Blocks.FlatGrassY;

    public GravityNoticeTests() => Blocks.EnsureLoaded();

    private static GravitySystem Gravity(IntentTestFixture fx) =>
        new(fx.World, fx.Players, fx.Context.EnvironmentPolicies);

    private static void Run(IntentTestFixture fx, GravitySystem gravity, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            gravity.Tick(fx.Clock, fx.Players.Online);
        }
    }

    [Fact]
    public void Any_write_that_removes_the_block_under_sand_makes_it_fall_after_the_delay()
    {
        var fx = new IntentTestFixture();
        fx.World.SetQuietly(0, Ground + 1, 0, Blocks.Stone);
        fx.World.SetQuietly(0, Ground + 2, 0, Blocks.Sand);
        var gravity = Gravity(fx);
        Run(fx, gravity, 5);
        Assert.Equal(Blocks.Sand, fx.World.GetBlock(0, Ground + 2, 0)); // supported: nothing happens
        Assert.Equal(0, fx.World.GravityPending.Count);

        Assert.True(fx.World.TrySetBlock(0, Ground + 1, 0, Blocks.Air)); // no player, no system: just a write

        Assert.Equal(1, fx.World.GravityPending.Count);
        Run(fx, gravity, 200);
        Assert.Equal(Blocks.Sand, fx.World.GetBlock(0, Ground + 1, 0));
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, Ground + 2, 0));
    }

    [Fact]
    public void A_write_that_changes_nothing_gravity_cares_about_notices_nothing()
    {
        var fx = new IntentTestFixture();
        Assert.True(fx.World.TrySetBlock(3, Ground + 5, 3, Blocks.Stone));
        Assert.True(fx.World.TrySetBlock(3, Ground + 5, 3, Blocks.Air));
        fx.World.SetQuietly(4, Ground + 1, 4, Blocks.Stone);
        fx.World.SetQuietly(4, Ground + 2, 4, Blocks.Sand);
        Assert.True(fx.World.TrySetBlock(4, Ground + 1, 4, Blocks.Air, SetBlockFlags.None)); // a quiet write is not noticed, as for neighbours

        Assert.Equal(0, fx.World.GravityPending.Count);
    }

    [Fact]
    public void A_creeper_blast_under_sand_drops_it_into_the_crater()
    {
        const int reach = 5;
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        var system = new Zenith.Gameplay.Entities.CreeperSystem(
            fx.World, fx.Players, new Zenith.Ecs.EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies, new Random(7));
        var id = system.SpawnCreeper(player.PositionX, player.PositionY, player.PositionZ + 1f);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(system.Stores.Positions.TryGet(id, out var at));
        var (cx, cy, cz) = ((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), (int)MathF.Floor(at.Z));
        for (var x = cx - reach; x <= cx + reach; x++)
            for (var y = cy - reach; y <= cy + reach; y++)
                for (var z = cz - reach; z <= cz + reach; z++)
                    _ = fx.World.SetQuietly(x, y, z, Blocks.Dirt);
        fx.World.SetQuietly(cx, cy + 3, cz, Blocks.Sand); // just outside the crater, resting on dirt that the blast removes
        Assert.Equal(0, fx.World.GravityPending.Count);

        fx.Clock.AdvanceBy(29);
        system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ExplosionCount);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(cx, cy + 2, cz)); // the support is gone
        Assert.True(fx.World.GravityPending.Count > 0, "the explosion made gravity notice the sand");

        Run(fx, Gravity(fx), 300);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(cx, cy + 3, cz));
        var sand = 0;
        for (var y = cy - reach; y <= cy + reach; y++) if (fx.World.GetBlock(cx, y, cz) == Blocks.Sand) sand++;
        Assert.Equal(1, sand);
    }

    [Fact]
    public void Leaves_that_decay_under_sand_let_it_fall()
    {
        var fx = new IntentTestFixture();
        fx.World.SetQuietly(8, Ground + 1, 8, Blocks.OakLeaves); // a world-grown leaf: no trunk, it will decay
        fx.World.SetQuietly(8, Ground + 2, 8, Blocks.Sand);
        var tick = new RandomTickContext(fx.World, new Random(1), [], fx.Context.EnvironmentPolicies);
        tick.Aim(8, Ground + 1, 8, Blocks.OakLeaves);
        new LeavesDecayBehavior().OnRandomTick(tick);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(8, Ground + 1, 8));

        Run(fx, Gravity(fx), 200);

        Assert.Equal(Blocks.Sand, fx.World.GetBlock(8, Ground + 1, 8));
        Assert.Equal(Blocks.Air, fx.World.GetBlock(8, Ground + 2, 8));
    }

    [Fact]
    public void Fire_that_burns_the_planks_under_gravel_lets_it_fall()
    {
        var fx = new IntentTestFixture();
        fx.World.SetQuietly(0, Ground + 1, 0, Blocks.OakPlanks);
        fx.World.SetQuietly(0, Ground + 2, 0, Blocks.Gravel);
        fx.World.SetQuietly(1, Ground + 1, 0, FireBlocks.WithAge(0));
        var blocks = new BlockUpdateSystem(fx.World, fx.Players, fx.Context.EnvironmentPolicies, new Random(5)); // fire is random: a fixed seed keeps the test from sometimes dying out early
        var gravity = Gravity(fx);
        _ = fx.World.BlockUpdates.Enqueue(1, Ground + 1, 0);

        for (var i = 0; i < 6000 && fx.World.GetBlock(0, Ground + 1, 0) != Blocks.Gravel; i++)
        {
            fx.Clock.AdvanceBy(1);
            blocks.Tick(fx.Clock, fx.Players.Online);
            gravity.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Equal(Blocks.Gravel, fx.World.GetBlock(0, Ground + 1, 0)); // the planks burned and the gravel came down onto the ground
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, Ground + 2, 0));
    }
}

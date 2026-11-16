using Xunit;
using Zenith.Gameplay.Policy;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>
/// Plan 003, phase B: a block that loses its support hangs for the start delay before it becomes a falling entity, and a stack peels
/// one block per delay. Support that comes back inside the delay cancels the fall. Nothing is ever lost on the way.
/// </summary>
public sealed class GravityStartDelayTests
{
    private const int Delay = GravityPendingStore.StartDelayTicks;
    private static readonly int Ground = Blocks.FlatGrassY;

    public GravityStartDelayTests() => Blocks.EnsureLoaded();

    private static (World.World World, GravitySystem Gravity, GameClock Clock) Boot()
    {
        var world = new World.World(new InMemoryChunkStorage());
        return (world, new GravitySystem(world, new PlayerManager(), new EnvironmentChangePolicies()), new GameClock());
    }

    private static void Tick(GravitySystem gravity, GameClock clock)
    {
        clock.AdvanceBy(1);
        gravity.Tick(clock, Array.Empty<Zenith.Player.Player>());
    }

    [Fact]
    public void Breaking_the_support_makes_the_sand_hang_for_the_delay_and_then_fall()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("digger");
        StandNear(player, 0, Ground + 3, 0);
        fx.World.SetBlock(0, Ground + 3, 0, Blocks.Stone);
        fx.World.SetQuietly(0, Ground + 4, 0, Blocks.Sand);
        EquipWoodenPickaxe(player);
        QueueReadyBreak(fx.Clock, fx.World, player, 0, Ground + 3, 0);
        var gravity = new GravitySystem(fx.World, fx.Players, fx.Context.EnvironmentPolicies);
        var edits = new Zenith.Gameplay.WorldInteraction.BlockEditSystem(fx.Players, fx.World, fx.Context.Durability);

        edits.Tick(fx.Clock, fx.Players.Online); // the break happens in this tick
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, Ground + 3, 0));
        var breakTick = fx.Clock.CurrentTick;

        for (var i = 0; i < Delay; i++)
        {
            gravity.Tick(fx.Clock, fx.Players.Online);
            Assert.Equal(Blocks.Sand, fx.World.GetBlock(0, Ground + 4, 0)); // still hanging, no entity
            Assert.Empty(fx.World.FallingBlocks.Active);
            if (i < Delay - 1) fx.Clock.AdvanceBy(1);
        }

        fx.Clock.AdvanceBy(1);
        gravity.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(breakTick + Delay, fx.Clock.CurrentTick);
        Assert.Single(fx.World.FallingBlocks.Active);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, Ground + 4, 0));
    }

    [Fact]
    public void Support_restored_inside_the_delay_cancels_the_fall()
    {
        var (world, gravity, clock) = Boot();
        world.SetQuietly(0, Ground + 2, 0, Blocks.Sand); // one air cell between it and the grass
        Assert.True(world.GravityPending.TryEnqueue(0, Ground + 2, 0, clock.CurrentTick + Delay));

        Tick(gravity, clock);
        world.SetBlock(0, Ground + 1, 0, Blocks.Stone);
        for (var i = 0; i < 10; i++) Tick(gravity, clock);

        Assert.Equal(Blocks.Sand, world.GetBlock(0, Ground + 2, 0));
        Assert.Empty(world.FallingBlocks.Active);
        Assert.Equal(0, world.GravityPending.Count);
    }

    [Fact]
    public void A_stack_peels_one_block_per_delay_and_every_block_lands()
    {
        var (world, gravity, clock) = Boot();
        for (var i = 0; i < 5; i++) world.SetQuietly(0, Ground + 3 + i, 0, Blocks.Sand); // floating, two air cells under the first
        Assert.True(world.GravityPending.TryEnqueue(0, Ground + 3, 0, clock.CurrentTick + Delay));

        var startedAt = new List<ulong>();
        var seen = 0;
        for (var i = 0; i < 400; i++)
        {
            Tick(gravity, clock);
            var sand = 0;
            for (var y = Ground + 1; y <= Ground + 9; y++) if (world.GetBlock(0, y, 0) == Blocks.Sand) sand++;
            Assert.Equal(5, sand + world.FallingBlocks.Active.Count); // conservation at every tick
            var inFlightOrLanded = world.FallingBlocks.Active.Count;
            if (inFlightOrLanded > seen) startedAt.Add(clock.CurrentTick);
            seen = Math.Max(seen, inFlightOrLanded);
            if (world.GravityPending.Count == 0 && world.FallingBlocks.Active.Count == 0) break;
        }

        Assert.True(world.GravityPending.Count == 0 && world.FallingBlocks.Active.Count == 0, "the stack settled");
        for (var y = Ground + 1; y <= Ground + 5; y++) Assert.Equal(Blocks.Sand, world.GetBlock(0, y, 0));
        Assert.True(startedAt.Count >= 2);
        for (var i = 1; i < startedAt.Count; i++)
            Assert.True(startedAt[i] - startedAt[i - 1] >= Delay, $"block {i} started {startedAt[i] - startedAt[i - 1]} ticks after the one below");
    }

    [Fact]
    public void A_large_collapse_is_bounded_per_tick_and_loses_nothing()
    {
        var (world, gravity, clock) = Boot();
        var total = GravityPendingStore.MaxStepsPerTick * 2 + 10;
        for (var i = 0; i < total; i++)
        {
            var x = i % 16; var z = i / 16;
            world.SetQuietly(x, Ground + 3, z, Blocks.Sand);
            Assert.True(world.GravityPending.TryEnqueue(x, Ground + 3, z, clock.CurrentTick + Delay));
        }

        for (var i = 0; i < Delay; i++) Tick(gravity, clock); // the tick the cells come due is the last of these
        Assert.Equal(GravityPendingStore.MaxStepsPerTick, world.FallingBlocks.Active.Count); // the bound, not more
        Tick(gravity, clock);
        Assert.True(world.FallingBlocks.Active.Count <= GravityPendingStore.MaxStepsPerTick * 2);

        for (var i = 0; i < 600 && (world.GravityPending.Count > 0 || world.FallingBlocks.Active.Count > 0); i++) Tick(gravity, clock);
        var landed = 0;
        for (var i = 0; i < total; i++)
            if (world.GetBlock(i % 16, Ground + 1, i / 16) == Blocks.Sand) landed++;
        Assert.Equal(total, landed);
    }

    [Fact]
    public void A_graceful_stop_inside_the_delay_settles_the_block_and_a_crash_loses_only_the_schedule()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        var gravity = new GravitySystem(world, new PlayerManager(), new EnvironmentChangePolicies());
        world.SetQuietly(0, Ground + 3, 0, Blocks.Sand);
        Assert.True(world.GravityPending.TryEnqueue(0, Ground + 3, 0, 1_000));

        world.PersistDirtyColumns(int.MaxValue);
        var crashed = new World.World(storage.Snapshot());
        Assert.Equal(Blocks.Sand, crashed.GetBlock(0, Ground + 3, 0)); // still exactly one block, only the schedule was lost
        Assert.Equal(0, crashed.GravityPending.Count);

        gravity.SettleAllPending();
        Assert.Equal(Blocks.Sand, world.GetBlock(0, Ground + 1, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(0, Ground + 3, 0));
        Assert.Equal(0, world.GravityPending.Count);
    }
}

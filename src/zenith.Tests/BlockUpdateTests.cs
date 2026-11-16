using Xunit;
using Zenith.Gameplay.WorldInteraction;
using Zenith.World;

namespace Zenith.Tests;

public class BlockUpdateTests
{
    private static int Id(string name)
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId(name, out var id), name);
        return id;
    }

    private static int With(int id, BitKey key, bool value)
    {
        Assert.True(Blocks.Catalog.States.TrySet(ref id, key, value));
        return id;
    }

    private static int With<T>(int id, EnumKey<T> key, T value) where T : unmanaged, Enum
    {
        Assert.True(Blocks.Catalog.States.TrySet(ref id, key, value));
        return id;
    }

    private static void Settle(IntentTestFixture fx, BlockUpdateSystem system, int ticks = 4)
    {
        for (var i = 0; i < ticks; i++) system.Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public void Update_queue_is_ordered_deduplicated_and_bounded()
    {
        var queue = new BlockUpdateQueue();
        Assert.True(queue.Enqueue(1, 2, 3));
        Assert.True(queue.Enqueue(4, 5, 6));
        Assert.True(queue.Enqueue(1, 2, 3));
        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryDequeue(out var x, out var y, out var z));
        Assert.Equal((1, 2, 3), (x, y, z));

        queue.Clear();
        for (var i = 0; i < BlockUpdateQueue.SoftCap; i++) Assert.True(queue.Enqueue(i, 0, 0));
        Assert.False(queue.Enqueue(-1, 0, 0));
        Assert.True(queue.Enqueue(0, 0, 0));
    }

    [Fact]
    public void Scheduled_ticks_pop_when_due_and_keep_the_earliest_request()
    {
        var ticks = new ScheduledTicks();
        Assert.True(ticks.Schedule(1, 1, 1, 10));
        Assert.True(ticks.Schedule(2, 2, 2, 5));
        Assert.True(ticks.Schedule(1, 1, 1, 20)); // later request for the same cell is ignored
        Assert.True(ticks.Schedule(3, 3, 3, 7));
        Assert.True(ticks.Schedule(3, 3, 3, 4)); // earlier one wins

        Assert.False(ticks.TryPopDue(3, out _, out _, out _));
        Assert.True(ticks.TryPopDue(5, out var x, out _, out _));
        Assert.Equal(3, x); // due 4
        Assert.True(ticks.TryPopDue(5, out x, out _, out _));
        Assert.Equal(2, x); // due 5
        Assert.False(ticks.TryPopDue(5, out _, out _, out _)); // the stale due-7 entry of cell 3 is gone, cell 1 not due
        Assert.True(ticks.TryPopDue(10, out x, out _, out _));
        Assert.Equal(1, x);
        Assert.False(ticks.TryPopDue(100, out _, out _, out _));
        Assert.Equal(0, ticks.Count);
    }

    [Fact]
    public void World_writes_feed_the_update_queue()
    {
        var fx = new IntentTestFixture();
        fx.World.BlockUpdates.Clear();
        Assert.True(fx.World.TrySetBlock(7, 70, 7, Blocks.Stone));
        Assert.True(fx.World.BlockUpdates.TryDequeue(out var x, out var y, out var z));
        Assert.Equal((7, 70, 7), (x, y, z));
    }

    [Fact]
    public void A_plant_breaks_when_its_soil_is_removed_and_stays_otherwise()
    {
        var fx = new IntentTestFixture();
        var system = new BlockUpdateSystem(fx.World, fx.Players);
        var grass = Id("minecraft:grass_block");
        var flower = Id("minecraft:dandelion");
        fx.World.TrySetBlock(3, 70, 3, grass);
        fx.World.TrySetBlock(3, 71, 3, flower);
        fx.World.TrySetBlock(5, 70, 3, Blocks.Stone);
        fx.World.TrySetBlock(5, 71, 3, flower); // a flower on stone has no soil
        Settle(fx, system);
        Assert.Equal(flower, fx.World.GetBlock(3, 71, 3));
        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(5, 71, 3));

        fx.World.TrySetBlock(3, 70, 3, World.World.AirRuntimeId);
        Settle(fx, system);
        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(3, 71, 3));
    }

    [Fact]
    public void A_door_without_ground_falls_apart_and_the_upper_half_follows_its_lower_half()
    {
        var fx = new IntentTestFixture();
        var system = new BlockUpdateSystem(fx.World, fx.Players);
        var lower = Id("minecraft:wooden_door");
        var upper = With(Id("minecraft:wooden_door"), StateKeys.UpperBlock, true);
        fx.World.TrySetBlock(9, 70, 9, Blocks.Stone);
        fx.World.TrySetBlock(9, 71, 9, lower);
        fx.World.TrySetBlock(9, 72, 9, upper);
        Settle(fx, system);
        Assert.Equal(lower, fx.World.GetBlock(9, 71, 9));
        Assert.Equal(upper, fx.World.GetBlock(9, 72, 9));

        fx.World.TrySetBlock(9, 70, 9, World.World.AirRuntimeId);
        Settle(fx, system);
        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(9, 71, 9));
        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(9, 72, 9));
    }

    [Fact]
    public void A_wall_torch_breaks_with_the_block_it_hangs_on_but_a_standing_torch_only_needs_ground()
    {
        var fx = new IntentTestFixture();
        var system = new BlockUpdateSystem(fx.World, fx.Players);
        // Placed by clicking the north face of the block at z+1: the torch cell is one north of it, facing "north".
        var wallTorch = With(Id("minecraft:torch"), StateKeys.TorchFacing, TorchFacing.North);
        var standing = With(Id("minecraft:torch"), StateKeys.TorchFacing, TorchFacing.Top);
        fx.World.TrySetBlock(2, 70, 11, Blocks.Stone);
        fx.World.TrySetBlock(2, 70, 10, wallTorch);
        fx.World.TrySetBlock(6, 69, 6, Blocks.Stone);
        fx.World.TrySetBlock(6, 70, 6, standing);
        Settle(fx, system);
        Assert.Equal(wallTorch, fx.World.GetBlock(2, 70, 10));
        Assert.Equal(standing, fx.World.GetBlock(6, 70, 6));

        fx.World.TrySetBlock(2, 70, 11, World.World.AirRuntimeId);
        fx.World.TrySetBlock(6, 69, 6, World.World.AirRuntimeId);
        Settle(fx, system);
        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(2, 70, 10));
        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(6, 70, 6));
    }

    [Fact]
    public void A_cascade_longer_than_one_tick_budget_continues_on_the_next_ticks()
    {
        var fx = new IntentTestFixture();
        var system = new BlockUpdateSystem(fx.World, fx.Players);
        var flower = Id("minecraft:dandelion");
        var count = BlockUpdateSystem.MaxChangedCellsPerTick + 40;
        for (var i = 0; i < count; i++)
        {
            fx.World.TrySetBlock(i, 70, 20, Blocks.Stone);
            fx.World.TrySetBlock(i, 71, 20, flower); // on stone: unsupported soil, but only reacts when evaluated
        }

        // Writes alone already queued every cell; the first tick may not reach them all.
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(fx.World.BlockUpdates.Count > 0);
        Settle(fx, system, ticks: 10);
        Assert.Equal(0, fx.World.BlockUpdates.Count);
        for (var i = 0; i < count; i++)
            Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(i, 71, 20));
    }
}

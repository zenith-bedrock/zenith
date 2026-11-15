using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class FloorDropTossTests
{
    [Fact]
    public void Successive_drops_launch_fresh_actors_instead_of_merging_at_the_old_anchor()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("successive-toss");
        player.Inventory.Clear(); player.Inventory.TrySetBlock(0, Blocks.Dirt, 2);
        for (var i = 0; i < 2; i++)
        {
            player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1551 + i,
                [InventoryStackAction.Drop(InventorySlotReference.Player(0), 1)]));
            fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        }
        Assert.True(player.Inventory.Get(0).IsEmpty);
        var drops = fx.World.FloorDrops.SnapshotViews();
        Assert.Equal(2, drops.Count); Assert.Equal(2, drops.Select(d => d.EntityRuntimeId).Distinct().Count());
        Assert.All(drops, drop => { Assert.True(drop.HasMotion); Assert.Equal(1, drop.Count); });
    }

    [Fact]
    public async Task Live_item_prevents_collision_state_eviction_until_removed()
    {
        var world = new World.World(new InMemoryChunkStorage());
        try
        {
            await world.GetOrCreateColumnAsync(0, 0);
            world.SetBlock(1, 67, 1, Blocks.Stone);
            world.FloorDrops.TryAddOrMerge(1, 68, 1, StackId.FromBlock(Blocks.Dirt), 1, 993, out _,
                motion: new FloorDropMotion(1.5f, 68, 1.5f, 0, 0, 0, true));
            Assert.False(world.TryEvictChunk(0, 0)); Assert.Equal(Blocks.Stone, world.GetBlock(1, 67, 1));
            Assert.True(world.FloorDrops.TryTake(1, 68, 1, out _, out _, out _));
            Assert.True(world.TryEvictChunk(0, 0));
        }
        finally { await world.StopGenerationAsync(); }
    }

    [Theory]
    [InlineData(0, 0, 0, 0, .4f)]
    [InlineData(90, 0, -.4f, 0, 0)]
    [InlineData(180, 0, 0, 0, -.4f)]
    [InlineData(0, -90, 0, .4f, 0)]
    [InlineData(0, 90, 0, -.4f, 0)]
    public void Launch_uses_yaw_and_pitch(float yaw, float pitch, float vx, float vy, float vz)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("direction");
        player.Yaw = yaw; player.Pitch = pitch;
        var body = FloorDropMotion.FromPlayer(player);
        Assert.InRange(MathF.Abs(body.VelocityX - vx), 0, .00001f);
        Assert.InRange(MathF.Abs(body.VelocityY - vy), 0, .00001f);
        Assert.InRange(MathF.Abs(body.VelocityZ - vz), 0, .00001f);
        Assert.InRange(body.Y - player.PositionY, 1.299f, 1.301f);
    }

    [Fact]
    public void Drop_flies_lands_and_is_picked_up_at_its_landing_position()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("flight");
        player.PositionX = .5f; player.PositionY = Blocks.FlatSpawnY; player.PositionZ = .5f;
        player.Yaw = 0; player.Pitch = 0; player.Inventory.Clear(); player.Inventory.TrySetBlock(0, Blocks.Dirt, 1);
        var client = new RecordingClientChannel(); player.AttachClient(client);
        player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1501, [InventoryStackAction.Drop(InventorySlotReference.Player(0), 1)]));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        var launch = Assert.Single(fx.World.FloorDrops.SnapshotViews());
        var sent = Assert.Single(client.Calls, c => c.Call == "SendFloorDropActor");
        Assert.InRange(Assert.IsType<float>(sent.Args[4]) - player.PositionY, 1.424f, 1.426f);
        Assert.True(launch.Pose.Z > player.PositionZ); Assert.True(launch.HasMotion);
        Assert.Equal(40, launch.PickupDelayTicks);
        var system = new FloorDropSystem(fx.World);
        for (var i = 0; i < 40; i++) system.Tick(fx.Clock, fx.Players.Online);
        var landed = Assert.Single(fx.World.FloorDrops.SnapshotViews());
        Assert.True(landed.Pose.Z > launch.Pose.Z + 2);
        Assert.InRange(landed.Pose.Y, player.PositionY - .001f, player.PositionY + .001f);
        Assert.True(landed.Pose.OnGround); Assert.Equal(0, landed.Pose.VelocityZ);
        Assert.True(player.Inventory.Get(0).IsEmpty); // Custody anchor at the feet is not pickup authority.
        Assert.Contains(client.Calls, c => c.Call == "SendMoveActorAbsoluteRaw");
        player.PositionX = landed.Pose.X; player.PositionZ = landed.Pose.Z;
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(fx.World.FloorDrops.Snapshot()); Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Swept_item_collision_stops_at_wall_and_ceiling()
    {
        var fx = new IntentTestFixture(); var y = Blocks.FlatSpawnY;
        for (var h = 0; h < 3; h++) fx.World.SetBlock(2, y + h, 0, Blocks.Stone);
        var body = new FloorDropMotion(.5f, y + 1.3f, .5f, .4f, 0, 0);
        for (var i = 0; i < 30; i++) body = FloorDropPhysics.Step(fx.World, body);
        Assert.InRange(body.X, 1.8f, 1.876f); Assert.Equal(0, body.VelocityX);
        fx.World.SetBlock(0, y + 2, 0, Blocks.Stone);
        body = new FloorDropMotion(.5f, y + 1.3f, .5f, 0, .4f, 0);
        for (var i = 0; i < 30; i++)
        {
            body = FloorDropPhysics.Step(fx.World, body);
            Assert.True(body.Y + EntityHitboxes.ItemEntitySize <= y + 2.001f);
        }
    }

    [Fact]
    public void Partial_pickup_republishes_the_physical_position()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("partial-toss");
        player.PositionX = 20.5f; player.PositionY = Blocks.FlatSpawnY; player.PositionZ = .5f;
        for (var i = 0; i < 36; i++) player.Inventory.TrySetBlock(i, Blocks.Stone, 64);
        player.Inventory.TrySetBlock(0, Blocks.Dirt, 63);
        var client = new RecordingClientChannel(); player.AttachClient(client);
        var pose = new FloorDropMotion(20.5f, player.PositionY, .5f, 0, 0, 0, true);
        Assert.True(fx.World.FloorDrops.TryAddOrMerge(0, (int)player.PositionY, 0, StackId.FromBlock(Blocks.Dirt), 2,
            990, out _, pickupDelayTicks: 0, motion: pose));
        new FloorDropSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(64, player.Inventory.Get(0).Count);
        Assert.Equal(1, Assert.Single(fx.World.FloorDrops.SnapshotViews()).Count);
        var sent = Assert.Single(client.Calls, c => c.Call == "SendFloorDropActor");
        Assert.Equal(20.5f, Assert.IsType<float>(sent.Args[3]));
    }

    [Fact]
    public async Task Column_item_catchup_reads_current_owner_state_after_terrain_worker_completes()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("owner-catchup");
        player.Chunks.Radius = -1; player.Chunks.RememberMany([(0, 0)]);
        try
        {
            var column = await fx.World.GetOrCreateColumnAsync(0, 0);
            Assert.True(player.Session.QueueWorldColumn(column, 0));
            Assert.True(SpinWait.SpinUntil(() => player.Session.WorldStreamIdle, TimeSpan.FromSeconds(5)));
            player.Session.Transport.FlushOutgoing(); while (fx.Transport.Captured.TryDequeue(out _)) { }
            fx.World.FloorDrops.TryAddOrMerge(1, 64, 1, StackId.FromBlock(Blocks.Dirt), 1, 991, out _);
            new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
            player.Session.Transport.FlushOutgoing(); Assert.Single(fx.Transport.Captured);
        }
        finally { await fx.World.StopGenerationAsync(); }
    }

    [Fact]
    public void Late_join_column_filter_uses_physical_position_not_custody_anchor()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("physical-column");
        fx.World.FloorDrops.TryAddOrMerge(0, 64, 0, StackId.FromBlock(Blocks.Dirt), 1, 992, out _,
            motion: new FloorDropMotion(32.5f, 64, .5f, 0, 0, 0));
        ColumnSend.EmitFloorDropsInColumn(player.Session, fx.World, 0, 0);
        player.Session.Transport.FlushOutgoing(); Assert.Empty(fx.Transport.Captured);
        ColumnSend.EmitFloorDropsInColumn(player.Session, fx.World, 2, 0);
        player.Session.Transport.FlushOutgoing(); Assert.Single(fx.Transport.Captured);
    }
}

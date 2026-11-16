using Zenith.Binary;
using Zenith.Packets;
using Zenith.Session;
using Zenith.Session.Handler;
using Xunit;

namespace Zenith.Tests;

public sealed class ChunkRadiusNegotiationTests
{
    [Fact]
    public async Task Post_spawn_terrain_and_gameplay_share_the_same_ordered_wire_lane()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("ordered-terrain");
        player.Session.SendDataPacket(new PlayStatusPacket { Status = PlayStatusPacket.PlayerSpawn });
        var column = await fx.World.GetOrCreateColumnAsync(0, 0);
        Assert.True(player.Session.QueueWorldColumn(column, NetworkSession.WorldStreamOrderChannel));
        Assert.True(SpinWait.SpinUntil(() => player.Session.WorldStreamIdle, TimeSpan.FromSeconds(10)));
        IntentTestHelpers.FlushRaknet(fx.Players);
        var frames = new List<Zenith.Raknet.Network.Frame>();
        foreach (var datagram in fx.Transport.Captured)
        {
            var stream = new BinaryStream(datagram, 1, datagram.Length - 1);
            var set = new Zenith.Raknet.Network.FrameSet();
            set.Decode(ref stream);
            frames.AddRange(set.Packets);
        }
        Assert.True(frames.Count >= 2);
        Assert.All(frames, frame =>
        {
            Assert.Equal((byte)0, frame.OrderChannel);
            Assert.Equal(Zenith.Raknet.Enumerator.Reliability.ReliableOrdered, frame.Reliability);
        });
        await fx.World.StopGenerationAsync();
    }

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(0, int.MaxValue, 4)]
    [InlineData(1, 2, 2)]
    [InlineData(2, 1, 1)]
    public async Task Radius_is_accepted_in_each_join_phase_and_applied_only_by_the_tick(int phase, int requested, int accepted)
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("radius");
        ISessionHandler handler = phase switch
        {
            0 => new PreSpawnSessionHandler(),
            1 => new SpawnResponseSessionHandler(),
            _ => new InGameSessionHandler()
        };
        player.Session.SetHandler(handler);
        player.Chunks.Radius = -1;
        var beforeTick = fx.Clock.CurrentTick;
        Assert.True(Request(handler, player.Session, requested));
        Assert.Equal(-1, player.Chunks.Radius);
        Assert.Equal(beforeTick, fx.Clock.CurrentTick);
        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(accepted, player.Chunks.Radius);
        await fx.World.StopGenerationAsync();
    }

    [Fact]
    public void Repeated_pre_spawn_requests_keep_one_load_and_the_latest_radius()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("radius-repeat");
        var handler = new PreSpawnSessionHandler();
        Assert.True(Request(handler, player.Session, 2));
        Assert.True(Request(handler, player.Session, 4));
        Assert.True(Request(handler, player.Session, 1));
        Assert.True(player.Chunks.TryConsumePreSpawnRequest(out var initial));
        Assert.Equal(2, initial.ViewRadius);
        Assert.False(player.Chunks.TryConsumePreSpawnRequest(out _));
        player.Chunks.ApplyPendingRadius();
        Assert.Equal(1, player.Chunks.Radius);
    }

    [Fact]
    public void Resize_without_movement_forces_a_fresh_publisher()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("radius-resize");
        player.Chunks.Radius = 4;
        Assert.True(player.Chunks.PublisherCenterChanged(0, 0));
        Assert.True(Request(new InGameSessionHandler(), player.Session, 1));
        Assert.Equal(4, player.Chunks.Radius);
        player.Chunks.ApplyPendingRadius();
        Assert.True(player.Chunks.PublisherCenterChanged(0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Camera_assist_control_is_recognized_without_a_gameplay_mutation(int phase)
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("camera-control");
        ISessionHandler handler = phase switch
        {
            0 => new PreSpawnSessionHandler(),
            1 => new SpawnResponseSessionHandler(),
            _ => new InGameSessionHandler()
        };
        var stream = new BinaryStream([]);
        Assert.True(handler.HandleDataPacket(player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.CLIENT_CAMERA_AIM_ASSIST_PACKET }, ref stream));
        Assert.Equal(0UL, fx.Clock.CurrentTick);
        Assert.Equal(0, fx.World.LoadedColumnCount);
    }

    private static bool Request(ISessionHandler handler, NetworkSession session, int radius)
    {
        var writer = new BinaryStream();
        writer.WriteVarInt(radius);
        writer.WriteByte(32);
        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        return handler.HandleDataPacket(session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.REQUEST_CHUNK_RADIUS_PACKET }, ref stream);
    }
}

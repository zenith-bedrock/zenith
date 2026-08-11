using Zenith.Gameplay.Systems;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class TickBatchEgressTests
{
    public TickBatchEgressTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Two_dirty_movers_one_envelope_per_watcher()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("a");
        var b = fx.AddInGamePlayer("b");
        var watcher = fx.AddInGamePlayer("watcher");
        var system = new MovementSystem(fx.Players);

        // Seed last-replicated with different baseline so both go dirty.
        a.SubmitMovementInput(MovementInputState.From(1f, Blocks.FlatSpawnY, 0f, 0f, 0f));
        b.SubmitMovementInput(MovementInputState.From(2f, Blocks.FlatSpawnY, 0f, 0f, 0f));
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        a.SubmitMovementInput(MovementInputState.From(1.5f, Blocks.FlatSpawnY, 0f, 0f, 0f));
        b.SubmitMovementInput(MovementInputState.From(2.5f, Blocks.FlatSpawnY, 0f, 0f, 0f));
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);

        // Watcher should get one datagram carrying both Absolutes; a gets b's; b gets a's.
        Assert.True(fx.Transport.Captured.Count >= 3,
            $"expected ≥3 peer frames (watcher+a+b), got {fx.Transport.Captured.Count}");
    }

    [Fact]
    public void PublishUpdateBlocks_packs_two_into_one_SendDataPacket()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("solo");
        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        player.Session.Protocol.World.PublishUpdateBlocks(
        [
            (1, -60, 2, Blocks.Dirt),
            (3, -60, 4, Blocks.Stone)
        ]);
        Flush(fx);

        Assert.Single(fx.Transport.Captured);
    }

    [Fact]
    public void SendMoveAbsolutes_two_poses_one_frame()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("solo");
        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        player.Session.Protocol.Entity.SendMoveAbsolutes(
        [
            new AbsoluteActorPose
            {
                ActorRuntimeId = 1,
                X = 0, Y = Blocks.FlatSpawnY, Z = 0,
                Pitch = 0, Yaw = 0, HeadYaw = 0
            },
            new AbsoluteActorPose
            {
                ActorRuntimeId = 2,
                X = 1, Y = Blocks.FlatSpawnY, Z = 1,
                Pitch = 0, Yaw = 90, HeadYaw = 90
            }
        ]);
        Flush(fx);

        Assert.Single(fx.Transport.Captured);
    }

    private static void Flush(IntentTestFixture fx)
    {
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();
    }
}

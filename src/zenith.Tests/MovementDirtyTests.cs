using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

public class MovementDirtyTests
{
    public MovementDirtyTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Identical_pose_second_tick_skips_Absolute_fanout()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        _ = fx.AddInGamePlayer("watcher");
        var system = new MovementSystem(fx.Players);

        SubmitPose(mover, 1f, Blocks.FlatSpawnY, 2f, pitch: 0f, yaw: 90f);
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        SubmitPose(mover, 1f, Blocks.FlatSpawnY, 2f, pitch: 0f, yaw: 90f);
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        Assert.Empty(fx.Transport.Captured);
    }

    [Fact]
    public void Pitch_only_change_fans_Absolute()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        _ = fx.AddInGamePlayer("watcher");
        var system = new MovementSystem(fx.Players);

        SubmitPose(mover, 1f, Blocks.FlatSpawnY, 2f, pitch: 0f, yaw: 90f);
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        SubmitPose(mover, 1f, Blocks.FlatSpawnY, 2f, pitch: 15f, yaw: 90f);
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "look-only AuthInput must fan Absolute to peers");
    }

    [Fact]
    public void Void_death_marks_dirty_and_fans_Absolute()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        _ = fx.AddInGamePlayer("watcher");
        var system = new MovementSystem(fx.Players);

        // Seed last-replicated at spawn so void Y change is dirty.
        SubmitPose(mover, 0f, Blocks.FlatSpawnY, 0f, 0f, 0f);
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        SubmitPose(mover, 32f, MovementSystem.VoidRescueY - 1f, -16f, 10f, 45f);
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "void death must fan Absolute to peers at death pose");
        Assert.True(mover.IsDead);
        Assert.Equal(32f, mover.PositionX);
        Assert.Equal(MovementSystem.VoidRescueY - 1f, mover.PositionY);
        Assert.Equal(-16f, mover.PositionZ);
    }

    [Fact]
    public void Respawn_after_void_death_fans_Absolute_to_spawn()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        _ = fx.AddInGamePlayer("watcher");
        var system = new MovementSystem(fx.Players);

        SubmitPose(mover, 32f, MovementSystem.VoidRescueY - 1f, -16f, 10f, 45f);
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        mover.SubmitRespawn();
        system.Tick(fx.Clock, fx.Players.Online);
        Flush(fx);
        Assert.False(mover.IsDead);
        Assert.Equal(0f, mover.PositionX);
        Assert.Equal(Blocks.FlatSpawnY, mover.PositionY);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "respawn must Absolute peers to world spawn");
    }

    private static void SubmitPose(Player.Player player, float x, float y, float z, float pitch, float yaw)
    {
        player.SubmitMovementInput(MovementInputState.From(x, y, z, pitch, yaw));
    }

    private static void Flush(IntentTestFixture fx)
    {
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();
    }
}

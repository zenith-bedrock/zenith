using Zenith.World;
using Xunit;
using Zenith.Gameplay.Replication;

namespace Zenith.Tests;

public class TimeSyncSystemTests
{
    public TimeSyncSystemTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Tick_zero_sends_time_to_online_players()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("alice");
        var system = new TimeSyncSystem();

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        system.Tick(fx.Clock, fx.Players.Online); // CurrentTick == 0 here, 0 % TicksPerSecond == 0
        FlushRaknet(fx.Players);

        Assert.Single(fx.Transport.Captured);
    }

    [Fact]
    public void Tick_between_seconds_does_not_send()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("alice");
        var system = new TimeSyncSystem();
        fx.Clock.AdvanceBy(1); // CurrentTick == 1, 1 % TicksPerSecond != 0

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        system.Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Empty(fx.Transport.Captured);
    }

    [Fact]
    public void Tick_on_next_second_boundary_sends_again()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("alice");
        var system = new TimeSyncSystem();
        fx.Clock.AdvanceBy(Zenith.Gameplay.Runtime.GameClock.TicksPerSecond); // CurrentTick == 20

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        system.Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Single(fx.Transport.Captured);
    }

    [Fact]
    public void Tick_with_no_online_players_is_a_no_op()
    {
        var fx = new IntentTestFixture();
        var system = new TimeSyncSystem();

        system.Tick(fx.Clock, Array.Empty<Zenith.Player.Player>()); // must not throw with an empty online list
        Assert.Empty(fx.Transport.Captured);
    }

    private static void FlushRaknet(Zenith.Player.PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }
}

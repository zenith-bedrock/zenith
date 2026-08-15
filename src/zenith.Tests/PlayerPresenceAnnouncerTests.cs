using Zenith.Event;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Replication;

namespace Zenith.Tests;

public class PlayerPresenceAnnouncerTests
{
    public PlayerPresenceAnnouncerTests() => Blocks.EnsureLoaded();

    [Fact]
    public void OnLogin_broadcasts_to_in_game_peers_but_not_to_the_joining_player()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        var joining = fx.AddPlayer("bob"); // registered, not yet InGame — matches real PlayerLoginEvent timing

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerPresenceAnnouncer.OnLogin(fx.Context, new PlayerLoginEvent(joining));
        FlushRaknet(fx.Players);

        Assert.Single(fx.Transport.Captured); // only alice (in-game); not bob (not in-game yet)
        _ = alice;
    }

    [Fact]
    public void OnQuit_was_in_game_broadcasts_to_remaining_peers()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        var leaving = fx.AddInGamePlayer("bob");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerPresenceAnnouncer.OnQuit(fx.Context, new PlayerQuitEvent(leaving, wasInGame: true));
        FlushRaknet(fx.Players);

        Assert.Single(fx.Transport.Captured); // alice only — leaving player is not in the peer loop
        _ = alice;
    }

    [Fact]
    public void OnQuit_pre_spawn_drop_does_not_broadcast()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("alice");
        var droppedMidLogin = fx.AddPlayer("bob");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerPresenceAnnouncer.OnQuit(fx.Context, new PlayerQuitEvent(droppedMidLogin, wasInGame: false));
        FlushRaknet(fx.Players);

        Assert.Empty(fx.Transport.Captured); // nobody saw them join — no "left the game" line
    }

    private static void FlushRaknet(Zenith.Player.PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }
}

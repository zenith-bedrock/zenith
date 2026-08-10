using Zenith.Gameplay;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class BlockSoundFanoutTests
{
    public BlockSoundFanoutTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Place_sends_to_subject_and_in_game_peer()
    {
        var fx = new IntentTestFixture();
        var placer = fx.AddInGamePlayer("placer");
        _ = fx.AddInGamePlayer("watcher");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        BlockSoundFanout.Place(fx.Players.Online, placer.Session, 1, -60, 2, Blocks.Stone);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 2, $"expected >=2 outbound frames (subject+peer), got {fx.Transport.Captured.Count}");
    }

    [Fact]
    public void Break_alone_sends_only_to_subject()
    {
        var fx = new IntentTestFixture();
        var breaker = fx.AddInGamePlayer("solo");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        BlockSoundFanout.Break(fx.Players.Online, breaker.Session, 3, -60, 4, Blocks.Dirt);
        FlushRaknet(fx.Players);

        Assert.Single(fx.Transport.Captured);
    }

    [Fact]
    public void Hit_does_not_reach_a_not_in_game_peer()
    {
        var fx = new IntentTestFixture();
        var miner = fx.AddInGamePlayer("miner");
        _ = fx.AddPlayer("prespawn"); // registered, not yet InGame

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        BlockSoundFanout.Hit(fx.Players.Online, miner.Session, 5, -60, 6, Blocks.Stone);
        FlushRaknet(fx.Players);

        Assert.Single(fx.Transport.Captured); // subject only — prespawn peer excluded
    }

    private static void FlushRaknet(Zenith.Player.PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }
}

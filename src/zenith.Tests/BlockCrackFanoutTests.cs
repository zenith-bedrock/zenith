using Zenith.Gameplay;
using Zenith.Network.Packets;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class BlockCrackFanoutTests
{
    public BlockCrackFanoutTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Start_fans_out_LevelEvent_to_miner_and_peer()
    {
        var fx = new IntentTestFixture();
        var miner = fx.AddInGamePlayer("miner");
        _ = fx.AddInGamePlayer("watcher");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        BlockCrackFanout.Start(
            fx.Players,
            miner.Session,
            blockX: 1,
            blockY: -60,
            blockZ: 2,
            breakTicks: Blocks.BreakTicks(Blocks.Dirt));
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 2,
            $"expected ≥2 outbound frames (miner+peer), got {fx.Transport.Captured.Count}");
    }

    [Fact]
    public void Stop_fans_out_to_both_players()
    {
        var fx = new IntentTestFixture();
        var miner = fx.AddInGamePlayer("miner");
        _ = fx.AddInGamePlayer("watcher");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        BlockCrackFanout.Stop(fx.Players, miner.Session, 3, -60, 4);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 2,
            $"expected ≥2 outbound frames (miner+peer), got {fx.Transport.Captured.Count}");
    }

    [Fact]
    public void Start_alone_sends_only_to_miner()
    {
        var fx = new IntentTestFixture();
        var miner = fx.AddInGamePlayer("solo");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        BlockCrackFanout.Start(fx.Players, miner.Session, 0, -60, 0, 15);
        FlushRaknet(fx.Players);

        Assert.Equal(1, fx.Transport.Captured.Count);
    }

    /// <summary>Priority.Normal queues frames; Tick flushes OutputFrames to Server.Send.</summary>
    private static void FlushRaknet(PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }
}

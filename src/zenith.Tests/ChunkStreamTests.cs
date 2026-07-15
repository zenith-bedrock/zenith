using Xunit;
using Zenith.Player;

namespace Zenith.Tests;

public class PlayerChunkTrackerTests
{
    [Fact]
    public void TryBegin_is_false_on_duplicate()
    {
        var t = new PlayerChunkTracker();
        Assert.True(t.TryBegin(1, 2));
        Assert.False(t.TryBegin(1, 2));
        t.Forget(1, 2);
        Assert.True(t.TryBegin(1, 2));
    }

    [Fact]
    public void ForEachInSquare_visits_expected_count()
    {
        var count = 0;
        PlayerChunkTracker.ForEachInSquare(0, 0, radius: 2, (_, _) => count++);
        Assert.Equal(25, count); // (2*2+1)^2
    }

    [Fact]
    public void PublisherCenterChanged_only_once_per_chunk()
    {
        var t = new PlayerChunkTracker();
        Assert.True(t.PublisherCenterChanged(3, 4));
        Assert.False(t.PublisherCenterChanged(3, 4));
        Assert.True(t.PublisherCenterChanged(3, 5));
    }

    [Fact]
    public void BlockToChunk_floors_negatives()
    {
        Assert.Equal(0, PlayerChunkTracker.BlockToChunk(0));
        Assert.Equal(0, PlayerChunkTracker.BlockToChunk(15.9f));
        Assert.Equal(1, PlayerChunkTracker.BlockToChunk(16f));
        Assert.Equal(-1, PlayerChunkTracker.BlockToChunk(-1f));
        Assert.Equal(-2, PlayerChunkTracker.BlockToChunk(-17f));
    }

    [Fact]
    public void RememberMany_prevents_restream()
    {
        var t = new PlayerChunkTracker();
        t.RememberMany([(0, 0), (1, 0)]);
        Assert.False(t.TryBegin(0, 0));
        Assert.False(t.TryBegin(1, 0));
        Assert.True(t.TryBegin(0, 1));
    }

    [Fact]
    public void ForgetOutsideRadius_allows_restream_on_reentry()
    {
        var t = new PlayerChunkTracker();
        Assert.True(t.TryBegin(0, 0, out _));
        Assert.True(t.TryBegin(5, 0, out _));
        t.ForgetOutsideRadius(centerX: 0, centerZ: 0, radius: 2);
        Assert.False(t.TryBegin(0, 0, out _)); // still in radius → still known
        Assert.True(t.TryBegin(5, 0, out _)); // forgotten → can begin again
    }

    [Fact]
    public void Stale_epoch_is_not_current_after_ForgetOutsideRadius()
    {
        var t = new PlayerChunkTracker();
        Assert.True(t.TryBegin(5, 0, out var epoch));
        Assert.True(t.IsStreamCurrent(5, 0, epoch));
        t.ForgetOutsideRadius(centerX: 0, centerZ: 0, radius: 2);
        Assert.False(t.IsStreamCurrent(5, 0, epoch));
        Assert.True(t.TryBegin(5, 0, out var epoch2));
        Assert.True(t.IsStreamCurrent(5, 0, epoch2));
        Assert.False(t.IsStreamCurrent(5, 0, epoch));
    }

    [Fact]
    public void TryAbandon_only_matches_epoch()
    {
        var t = new PlayerChunkTracker();
        Assert.True(t.TryBegin(1, 1, out var epoch));
        Assert.False(t.TryAbandon(1, 1, epoch + 1));
        Assert.True(t.TryAbandon(1, 1, epoch));
        Assert.True(t.TryBegin(1, 1, out _));
    }
}

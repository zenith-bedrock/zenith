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
}

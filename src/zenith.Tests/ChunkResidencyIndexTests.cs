using System.Threading.Tasks;
using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public sealed class ChunkResidencyIndexTests
{
    [Fact]
    public void A_chunk_with_no_Acquire_has_no_viewers()
    {
        var index = new ChunkResidencyIndex();
        Assert.False(index.HasViewers(0, 0));
        Assert.Equal(0, index.ViewerCount(0, 0));
    }

    [Fact]
    public void Acquire_then_Release_returns_to_no_viewers()
    {
        var index = new ChunkResidencyIndex();
        index.Acquire(1, 2);
        Assert.True(index.HasViewers(1, 2));
        Assert.Equal(1, index.ViewerCount(1, 2));

        index.Release(1, 2);
        Assert.False(index.HasViewers(1, 2));
        Assert.Equal(0, index.ViewerCount(1, 2));
    }

    [Fact]
    public void Multiple_viewers_of_the_same_chunk_are_refcounted()
    {
        var index = new ChunkResidencyIndex();
        index.Acquire(0, 0);
        index.Acquire(0, 0);
        Assert.Equal(2, index.ViewerCount(0, 0));

        index.Release(0, 0);
        Assert.True(index.HasViewers(0, 0)); // one viewer left

        index.Release(0, 0);
        Assert.False(index.HasViewers(0, 0));
    }

    [Fact]
    public void Release_below_zero_clamps_at_zero_instead_of_going_negative()
    {
        var index = new ChunkResidencyIndex();
        index.Release(3, 3); // no prior Acquire — must not underflow
        Assert.Equal(0, index.ViewerCount(3, 3));
        Assert.False(index.HasViewers(3, 3));

        index.Acquire(3, 3);
        Assert.Equal(1, index.ViewerCount(3, 3));
    }

    [Fact]
    public void Different_chunks_are_tracked_independently()
    {
        var index = new ChunkResidencyIndex();
        index.Acquire(0, 0);
        Assert.True(index.HasViewers(0, 0));
        Assert.False(index.HasViewers(1, 0));
    }

    /// <summary>Acquire/Release can race across the GameLoop thread (ChunkStreamSystem) and a
    /// network thread (PlayerQuitEvent disconnect cleanup) — must never lose or underflow counts.</summary>
    [Fact]
    public async Task Concurrent_Acquire_and_Release_settle_to_a_consistent_count()
    {
        var index = new ChunkResidencyIndex();
        const int workers = 8;
        const int acquiresPerWorker = 500;

        var tasks = new Task[workers];
        for (var i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (var j = 0; j < acquiresPerWorker; j++)
                    index.Acquire(7, 7);
            });
        }
        await Task.WhenAll(tasks);

        Assert.Equal(workers * acquiresPerWorker, index.ViewerCount(7, 7));

        for (var i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (var j = 0; j < acquiresPerWorker; j++)
                    index.Release(7, 7);
            });
        }
        await Task.WhenAll(tasks);

        Assert.Equal(0, index.ViewerCount(7, 7));
        Assert.False(index.HasViewers(7, 7));
    }
}

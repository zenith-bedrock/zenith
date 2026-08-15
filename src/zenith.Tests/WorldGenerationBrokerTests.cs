using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public sealed class WorldGenerationBrokerTests
{
    [Fact]
    public async Task Broker_deduplicates_requests_and_limits_generation_workers()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        await using var broker = new WorldGenerationBroker(
            workerCount: 1,
            async coordinate =>
            {
                var current = Interlocked.Increment(ref active);
                while (true)
                {
                    var previous = Volatile.Read(ref maximumActive);
                    if (current <= previous || Interlocked.CompareExchange(ref maximumActive, current, previous) == previous)
                        break;
                }

                if (coordinate.X == 0)
                    firstStarted.SetResult();
                await release.Task.ConfigureAwait(false);
                Interlocked.Decrement(ref active);
                return new ColumnReadResult(
                    new ChunkColumnData(coordinate, 0, 0, Array.Empty<byte>()),
                    Array.Empty<BlockOverride>());
            });

        var first = await broker.RequestAsync(new ChunkCoord(0, 0));
        Assert.False(first.Coalesced);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var duplicate = await broker.RequestAsync(new ChunkCoord(0, 0));
        Assert.True(duplicate.Coalesced);
        Assert.Same(first.Task, duplicate.Task);

        var second = await broker.RequestAsync(new ChunkCoord(1, 0));
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref maximumActive));
        Assert.False(second.Task.IsCompleted);

        release.SetResult();
        await first.Task;
        await second.Task;
        Assert.Equal(1, Volatile.Read(ref maximumActive));
    }
}

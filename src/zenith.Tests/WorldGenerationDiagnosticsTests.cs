using Zenith.Diagnostics;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public sealed class WorldGenerationDiagnosticsTests
{
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void WorldGenerationDiagnostics_columnScope_recordsRequestAndReturnsInFlightToZero()
    {
        var (diagnostics, runtime) = CreateDiagnostics();

        using (diagnostics.BeginColumn())
            diagnostics.RecordGenerated();

        var snapshot = runtime.CaptureSnapshot();

        Assert.Equal(1L, Value(snapshot, "gameplay.worldgen.columns.requested"));
        Assert.Equal(1L, Value(snapshot, "gameplay.worldgen.columns.generated"));
        Assert.Equal(0L, Value(snapshot, "gameplay.worldgen.columns.in-flight"));
        Assert.Equal(1L, Count(snapshot, "gameplay.worldgen.column"));
    }

    [Fact]
    public void WorldGenerationDiagnostics_preserves_queue_peak_after_queue_drains()
    {
        var (diagnostics, runtime) = CreateDiagnostics();

        diagnostics.RecordQueueDepth(3);
        diagnostics.RecordQueueDepth(1);

        var snapshot = runtime.CaptureSnapshot();

        Assert.Equal(1L, Value(snapshot, "gameplay.worldgen.queue.depth"));
        Assert.Equal(3L, Value(snapshot, "gameplay.worldgen.queue.peak"));
    }

    [Fact]
    public void WorldGenerationDiagnostics_recordsPreSpawnPublicationCounters()
    {
        var (diagnostics, runtime) = CreateDiagnostics();

        using (diagnostics.BeginPreSpawnLoad())
        using (diagnostics.BeginPreSpawnPublish())
            diagnostics.RecordPreSpawnPublished(columns: 3, bytes: 4096);

        var snapshot = runtime.CaptureSnapshot();

        Assert.Equal(3L, Value(snapshot, "gameplay.worldgen.pre-spawn.columns"));
        Assert.Equal(4096L, Value(snapshot, "gameplay.worldgen.pre-spawn.bytes"));
        Assert.Equal(1L, Count(snapshot, "gameplay.worldgen.pre-spawn.load"));
        Assert.Equal(1L, Count(snapshot, "gameplay.worldgen.pre-spawn.publish"));
    }

    [Fact]
    public async Task World_coalesces_concurrent_column_generation_without_sharing_cancellation()
    {
        var (diagnostics, runtime) = CreateDiagnostics();
        using var terrain = new BlockingTerrainProvider();
        var world = new World.World(
            new InMemoryChunkStorage(),
            terrain: terrain,
            generationDiagnostics: diagnostics);

        var first = Task.Run(async () => await world.GetOrCreateColumnAsync(0, 0));
        Assert.True(terrain.Started.Wait(CoordinationTimeout));

        using var secondCancellation = new CancellationTokenSource();
        var second = Task.Run(async () =>
            await world.GetOrCreateColumnAsync(0, 0, secondCancellation.Token));
        Assert.True(SpinWait.SpinUntil(
            () => Value(runtime.CaptureSnapshot(), "gameplay.worldgen.columns.requested") == 2,
            CoordinationTimeout));
        secondCancellation.Cancel();
        terrain.Release.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second);
        await first;

        var snapshot = runtime.CaptureSnapshot();
        Assert.Equal(2L, Value(snapshot, "gameplay.worldgen.columns.requested"));
        Assert.Equal(1L, Value(snapshot, "gameplay.worldgen.columns.generated"));
        Assert.Equal(1L, Value(snapshot, "gameplay.worldgen.columns.coalesced"));
        Assert.Equal(0L, Value(snapshot, "gameplay.worldgen.columns.in-flight"));
    }

    [Fact]
    public async Task World_reuses_bounded_base_column_cache_after_generation_completes()
    {
        var (diagnostics, runtime) = CreateDiagnostics();
        var terrain = new CountingTerrainProvider();
        var world = new World.World(
            new InMemoryChunkStorage(),
            terrain: terrain,
            generationDiagnostics: diagnostics,
            generationCacheColumns: 1);

        await world.GetOrCreateColumnAsync(3, 4);
        await world.GetOrCreateColumnAsync(3, 4);

        var snapshot = runtime.CaptureSnapshot();
        Assert.Equal(1, terrain.Calls);
        Assert.Equal(1L, Value(snapshot, "gameplay.worldgen.columns.generated"));
        Assert.Equal(1L, Value(snapshot, "gameplay.worldgen.columns.cache-hit"));
    }

    private static (WorldGenerationDiagnostics Diagnostics, DiagnosticsRuntime Runtime) CreateDiagnostics()
    {
        var builder = new DiagnosticsBuilder();
        var requested = builder.Counter("gameplay.worldgen.columns.requested");
        var generated = builder.Counter("gameplay.worldgen.columns.generated");
        var loaded = builder.Counter("gameplay.worldgen.columns.loaded");
        var failed = builder.Counter("gameplay.worldgen.columns.failed");
        var coalesced = builder.Counter("gameplay.worldgen.columns.coalesced");
        var cacheHit = builder.Counter("gameplay.worldgen.columns.cache-hit");
        var dropped = builder.Counter("gameplay.worldgen.columns.dropped");
        var inFlight = builder.Gauge("gameplay.worldgen.columns.in-flight");
        var column = builder.Timing("gameplay.worldgen.column");
        var surface = builder.Timing("gameplay.worldgen.column.surface");
        var caves = builder.Timing("gameplay.worldgen.column.caves");
        var features = builder.Timing("gameplay.worldgen.column.features");
        var payload = builder.Timing("gameplay.worldgen.column.payload");
        var sampling = builder.Timing("gameplay.worldgen.column.sampling");
        var encode = builder.Timing("gameplay.worldgen.column.encode");
        var preSpawnLoad = builder.Timing("gameplay.worldgen.pre-spawn.load");
        var preSpawnPublish = builder.Timing("gameplay.worldgen.pre-spawn.publish");
        var preSpawnColumns = builder.Counter("gameplay.worldgen.pre-spawn.columns");
        var preSpawnBytes = builder.Counter("gameplay.worldgen.pre-spawn.bytes");
        var queueWait = builder.Timing("gameplay.worldgen.queue.wait");
        var queueBackpressure = builder.Counter("gameplay.worldgen.queue.backpressure");
        var queueDepth = builder.Gauge("gameplay.worldgen.queue.depth");
        var queuePeak = builder.Gauge("gameplay.worldgen.queue.peak");
        var runtime = builder.Build();
        var diagnostics = new WorldGenerationDiagnostics(
            runtime, requested, generated, loaded, failed, coalesced, cacheHit, dropped, inFlight, column,
            surface, caves, features, payload,
            sampling, encode,
            preSpawnLoad, preSpawnPublish, preSpawnColumns, preSpawnBytes,
            queueWait, queueBackpressure, queueDepth, queuePeak);
        return (diagnostics, runtime);
    }

    private static long Value(DiagnosticsSnapshot snapshot, string name) =>
        snapshot.Metrics.Single(metric => metric.Name == name).Value;

    private static long Count(DiagnosticsSnapshot snapshot, string name) =>
        snapshot.Metrics.Single(metric => metric.Name == name).Count;

    private sealed class BlockingTerrainProvider : ITerrainProvider, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public TerrainColumn GetBaseColumn(int chunkX, int chunkZ)
        {
            _ = chunkX;
            _ = chunkZ;
            Started.Set();
            Release.Wait();
            return new TerrainColumn(1, [1]);
        }

        public int SampleBaseBlock(int x, int y, int z) => 0;
        public int SampleSpawnFeetY(int x, int z) => 0;
        public SpawnBiome SampleSpawnBiome(int x, int z) => SpawnBiome.Plains;

        public void Dispose()
        {
            Release.Set();
            Started.Dispose();
            Release.Dispose();
        }
    }

    private sealed class CountingTerrainProvider : ITerrainProvider
    {
        public int Calls;

        public TerrainColumn GetBaseColumn(int chunkX, int chunkZ)
        {
            _ = chunkX;
            _ = chunkZ;
            Interlocked.Increment(ref Calls);
            return new TerrainColumn(1, [1]);
        }

        public int SampleBaseBlock(int x, int y, int z) => 0;
        public int SampleSpawnFeetY(int x, int z) => 0;
        public SpawnBiome SampleSpawnBiome(int x, int z) => SpawnBiome.Plains;
    }
}

using BenchmarkDotNet.Attributes;
using Zenith.Packets;
using Zenith.World;

namespace Zenith.Benchmarks;

/// <summary>
/// Per-column worldgen + LevelChunk encode (ADR §63–§69).
/// Filter: <c>-f *WorldgenColumn*</c>. Not a CI gate (ADR §24).
/// </summary>
[MemoryDiagnoser]
[HideColumns("Job", "Error", "StdDev", "Median", "RatioSD")]
public class WorldgenColumnBenchmarks
{
    private const int Seed = 42;

    private NoiseTerrainProvider _noise = null!;
    private LevelChunkPacket _flatPacket = null!;
    private LevelChunkPacket _noisePacket = null!;
    private OverworldCaveContext _caveQueries = null!;
    private int _chunkCursor;

    [GlobalSetup]
    public void Setup()
    {
        Blocks.EnsureLoaded();
        _noise = new NoiseTerrainProvider(Seed);
        _caveQueries = OverworldCaveContext.ForColumn(0, 0, Seed);

        var (flatCount, flatPayload) = ChunkPayloads.BuildFlatOverworld();
        _flatPacket = new LevelChunkPacket
        {
            ChunkX = 0,
            ChunkZ = 0,
            DimensionId = DimensionId.Overworld,
            SubChunkCount = flatCount,
            ExtraPayload = flatPayload
        };

        var noise = _noise.GetBaseColumn(0, 0);
        _noisePacket = new LevelChunkPacket
        {
            ChunkX = 0,
            ChunkZ = 0,
            DimensionId = DimensionId.Overworld,
            SubChunkCount = noise.SubChunkCount,
            ExtraPayload = noise.Payload
        };
    }

    /// <summary>Rebuild flat paletted column (not the cached <see cref="FlatTerrainProvider"/> singleton).</summary>
    [Benchmark(Baseline = true)]
    public int Flat_BuildOverworldColumn()
    {
        var (count, payload) = ChunkPayloads.BuildFlatOverworld();
        return count + payload.Length;
    }

    /// <summary>Full noise column (caves + single-pass payload, ADR §69).</summary>
    [Benchmark]
    public int Noise_GetBaseColumn()
    {
        var x = _chunkCursor++;
        return _noise.GetBaseColumn(x, 0).Payload.Length;
    }

    /// <summary>Cave segment grid only — isolate CSR cost from paletted write.</summary>
    [Benchmark]
    public int Noise_CaveContextOnly()
    {
        var x = _chunkCursor++;
        using var ctx = OverworldCaveContext.ForColumn(x, 0, Seed);
        return ctx.SegmentCount;
    }

    /// <summary>Spatial cave query cost over the same 16x16x96 sample window.</summary>
    [Benchmark]
    public int Noise_CaveSpatialQueries()
    {
        var carved = 0;
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = -32; y < 64; y++)
            if (_caveQueries.IsCarved(x, y, z, surfaceY: 80)) carved++;
        return carved;
    }

    /// <summary>Reference cost for the same query window; validates the spatial index payoff.</summary>
    [Benchmark]
    public int Noise_CaveBruteForceQueries()
    {
        var carved = 0;
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = -32; y < 64; y++)
            if (_caveQueries.IsCarvedBruteForce(x, y, z, surfaceY: 80)) carved++;
        return carved;
    }

    /// <summary>Legacy ore path: hashes neighboring vein cells for every host voxel.</summary>
    [Benchmark]
    public int Noise_OreLegacyQueries()
    {
        var ores = 0;
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = Blocks.FlatMinY; y <= OverworldOrePlacer.ColumnMaxOreY; y++)
        {
            var host = y < 0 ? Blocks.Deepslate : Blocks.Stone;
            if (OverworldOrePlacer.TryReplaceHost(host, x, y, z, Seed) != 0) ores++;
        }
        return ores;
    }

    /// <summary>Column DOD ore path: hashes each neighboring vein cell once.</summary>
    [Benchmark]
    public int Noise_OrePlannedQueries()
    {
        Span<OreCell> cells = stackalloc OreCell[OverworldOrePlacer.MaxColumnCellCount];
        var count = OverworldOrePlacer.FillColumnCells(
            0, 0, Seed, cells,
            out var minCellX, out var minCellY, out var minCellZ,
            out var widthX, out var widthY, out var widthZ);
        var view = cells[..count];
        var ores = 0;
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = Blocks.FlatMinY; y <= OverworldOrePlacer.ColumnMaxOreY; y++)
        {
            if (OverworldOrePlacer.TryReplaceHost(
                    y < 0, x, y, z, view,
                    minCellX, minCellY, minCellZ, widthX, widthY, widthZ) != 0)
                ores++;
        }
        return ores;
    }

    [GlobalCleanup]
    public void Cleanup() => _caveQueries.Dispose();

    /// <summary>Wire encode of a pre-built flat LevelChunk.</summary>
    [Benchmark]
    public int Encode_FlatLevelChunk() => _flatPacket.Encode().Length;

    /// <summary>Wire encode of a pre-built noise LevelChunk (ExtraPayload ≫ flat).</summary>
    [Benchmark]
    public int Encode_NoiseLevelChunk() => _noisePacket.Encode().Length;
}

/// <summary>
/// PreSpawn-shaped parallel disk (<see cref="World.World.GetRadiusAsync"/>).
/// Radius 2 = 25 columns; radius 4 = 81 (default <c>spawn-chunk-radius</c>).
/// Filter: <c>-f *WorldgenPreSpawn*</c> or <c>-f *Worldgen*</c>.
/// </summary>
[MemoryDiagnoser]
[HideColumns("Job", "Error", "StdDev", "Median", "RatioSD")]
public class WorldgenPreSpawnBenchmarks
{
    private global::Zenith.World.World _noiseWorld = null!;
    private int _centerCursor;

    [Params(2, 4)]
    public int Radius { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Blocks.EnsureLoaded();
        _noiseWorld = new global::Zenith.World.World(
            new InMemoryChunkStorage(),
            terrain: new NoiseTerrainProvider(seed: 42));
    }

    /// <summary>
    /// InMemory + §45 miss → regenerates every call (true gen cost, parallel).
    /// </summary>
    [Benchmark]
    public async Task<int> Noise_GetRadiusAsync()
    {
        var center = _centerCursor++ * (Radius * 2 + 3);
        var columns = await _noiseWorld.GetRadiusAsync(center, 0, Radius).ConfigureAwait(false);
        return columns.Count;
    }
}

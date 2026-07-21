using BenchmarkDotNet.Attributes;
using Zenith.Packets;
using Zenith.World;

namespace Zenith.Benchmarks;

/// <summary>
/// Dominant join/stream payload encode. Prefer <see cref="WorldgenBenchmarks"/> for gen+encode suite.
/// </summary>
[MemoryDiagnoser]
public class LevelChunkEncodeBenchmarks
{
    private LevelChunkPacket _flat = null!;
    private LevelChunkPacket _noise = null!;

    [GlobalSetup]
    public void Setup()
    {
        Blocks.EnsureLoaded();
        var (flatCount, flatPayload) = ChunkPayloads.BuildFlatOverworld();
        _flat = new LevelChunkPacket
        {
            ChunkX = 0,
            ChunkZ = 0,
            DimensionId = DimensionId.Overworld,
            SubChunkCount = flatCount,
            ExtraPayload = flatPayload
        };

        var noise = new NoiseTerrainProvider(seed: 42).GetBaseColumn(0, 0);
        _noise = new LevelChunkPacket
        {
            ChunkX = 0,
            ChunkZ = 0,
            DimensionId = DimensionId.Overworld,
            SubChunkCount = noise.SubChunkCount,
            ExtraPayload = noise.Payload
        };
    }

    [Benchmark(Baseline = true)]
    public int EncodeFlatColumn() => _flat.Encode().Length;

    [Benchmark]
    public int EncodeNoiseColumn() => _noise.Encode().Length;
}

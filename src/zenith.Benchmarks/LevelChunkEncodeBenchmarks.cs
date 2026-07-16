using BenchmarkDotNet.Attributes;
using Zenith.Packets;
using Zenith.World;

namespace Zenith.Benchmarks;

/// <summary>Dominant join/stream payload: flat LevelChunk encode.</summary>
[MemoryDiagnoser]
public class LevelChunkEncodeBenchmarks
{
    private LevelChunkPacket _packet = null!;

    [GlobalSetup]
    public void Setup()
    {
        Blocks.EnsureLoaded();
        var (subChunkCount, payload) = ChunkPayloads.BuildFlatOverworld();
        _packet = new LevelChunkPacket
        {
            ChunkX = 0,
            ChunkZ = 0,
            DimensionId = DimensionId.Overworld,
            SubChunkCount = subChunkCount,
            ExtraPayload = payload
        };
    }

    [Benchmark]
    public int EncodeFlatColumn() => _packet.Encode().Length;
}

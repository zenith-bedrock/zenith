using BenchmarkDotNet.Attributes;
using Zenith.Network;
using Zenith.Network.Packets;

namespace Zenith.Benchmarks;

[MemoryDiagnoser]
public class GamePacketBenchmarks
{
    private GamePacket _batch = null!;

    [GlobalSetup]
    public void Setup()
    {
        _batch = new GamePacket
        {
            Compression = PacketCompression.NONE,
            CompressionThreshold = 256,
            Packets =
            [
                new PlayStatusPacket { Status = 3 },
                new PlayStatusPacket { Status = 0 },
                new PlayStatusPacket { Status = 1 },
            ]
        };
    }

    [Benchmark]
    public int EncodeSmallBatch() => _batch.Encode().Length;
}

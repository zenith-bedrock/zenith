using BenchmarkDotNet.Attributes;
using Zenith.Packets;
using Zenith.Protocol;

namespace Zenith.Benchmarks;

/// <summary>
/// GamePacket encode: tiny NONE batch (legacy signal) vs ZLIB under/over threshold.
/// </summary>
[MemoryDiagnoser]
public class GamePacketBenchmarks
{
    private GamePacket _smallNone = null!;
    private GamePacket _zlibUnder = null!;
    private GamePacket _zlibOver = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallNone = new GamePacket
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

        // Few UpdateBlocks → uncompressed payload under default 256 threshold → NONE raw branch.
        _zlibUnder = new GamePacket
        {
            Compression = PacketCompression.ZLIB,
            CompressionThreshold = 256,
            Packets = BuildUpdateBlocks(2)
        };

        // Enough UpdateBlocks to force Deflate Fastest.
        _zlibOver = new GamePacket
        {
            Compression = PacketCompression.ZLIB,
            CompressionThreshold = 256,
            Packets = BuildUpdateBlocks(32)
        };
    }

    [Benchmark(Description = "Encode 3× PlayStatus Compression=NONE")]
    public int EncodeSmallBatchNone() => _smallNone.Encode().Length;

    [Benchmark(Description = "Encode 2× UpdateBlock ZLIB under threshold")]
    public int EncodeZlibUnderThreshold() => _zlibUnder.Encode().Length;

    [Benchmark(Description = "Encode 32× UpdateBlock ZLIB over threshold")]
    public int EncodeZlibOverThreshold() => _zlibOver.Encode().Length;

    private static List<DataPacket> BuildUpdateBlocks(int count)
    {
        var list = new List<DataPacket>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new UpdateBlockPacket
            {
                X = i,
                Y = -60,
                Z = 0,
                BlockRuntimeId = 1 + (i % 7),
                Flags = UpdateBlockPacket.FlagNeighborsAndNetwork
            });
        }

        return list;
    }
}

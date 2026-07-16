using BenchmarkDotNet.Attributes;
using Zenith.Packets;
using Zenith.Protocol;

namespace Zenith.Benchmarks;

/// <summary>Place/break fan-out shape: N UpdateBlocks in one GamePacket (ADR §44).</summary>
[MemoryDiagnoser]
public class UpdateBlockBatchBenchmarks
{
    private GamePacket _none = null!;
    private GamePacket _zlib = null!;

    [Params(1, 8, 32)]
    public int BlockCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var packets = BuildUpdateBlocks(BlockCount);
        _none = new GamePacket
        {
            Compression = PacketCompression.NONE,
            CompressionThreshold = 256,
            Packets = packets
        };
        _zlib = new GamePacket
        {
            Compression = PacketCompression.ZLIB,
            CompressionThreshold = 256,
            Packets = packets
        };
    }

    [Benchmark]
    public int EncodeNone() => _none.Encode().Length;

    [Benchmark]
    public int EncodeZlibPref() => _zlib.Encode().Length;

    private static List<DataPacket> BuildUpdateBlocks(int count)
    {
        var list = new List<DataPacket>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new UpdateBlockPacket
            {
                X = i % 16,
                Y = -60,
                Z = i / 16,
                BlockRuntimeId = 1 + (i % 7),
                Flags = UpdateBlockPacket.FlagNeighborsAndNetwork
            });
        }

        return list;
    }
}

using BenchmarkDotNet.Attributes;
using Zenith.Raknet.Stream;

namespace Zenith.Benchmarks;

[MemoryDiagnoser]
public class BinaryStreamBenchmarks
{
    private byte[] _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        var w = new BinaryStream();
        for (var i = 0; i < 64; i++)
        {
            w.WriteUnsignedVarInt(i);
            w.WriteInt(i);
        }

        _payload = w.GetBufferDisposing().ToArray();
    }

    [Benchmark]
    public int WriteVarIntsAndInts()
    {
        var w = new BinaryStream();
        for (var i = 0; i < 64; i++)
        {
            w.WriteUnsignedVarInt(i);
            w.WriteInt(i);
        }

        return w.GetBufferDisposing().Length;
    }

    [Benchmark]
    public int ReadVarIntsAndInts()
    {
        var r = new BinaryStream(_payload);
        var sum = 0;
        for (var i = 0; i < 64; i++)
        {
            sum += r.ReadUnsignedVarInt();
            sum += r.ReadInt();
        }

        return sum;
    }
}

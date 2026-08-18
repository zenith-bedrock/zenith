using System.Net;
using BenchmarkDotNet.Attributes;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;

namespace Zenith.Benchmarks;

/// <summary>
/// ADR §126 — measures the actual allocation delta from slicing (<c>Frame.Buffer.Slice</c>) instead
/// of copying (<c>.AsSpan(...).ToArray()</c>) each fragment in <see cref="RakNetSession"/>'s outbound
/// split loop. <c>Fragment_Slice</c> exercises the real current code via <c>SendFrame</c>;
/// <c>Fragment_CopyBaseline</c> reproduces the pre-fix per-fragment-copy behavior directly (not by
/// reverting production code) as a side-by-side baseline for the same payload sizes. This is the
/// "D — outbound fragmentation" benchmark <c>docs/history/phases/phase-xxvii-network-hot-path-findings.md</c>
/// explicitly scoped out and never built.
/// </summary>
[MemoryDiagnoser]
public class OutboundFragmentationBenchmarks
{
    private sealed class NullServer : RakNetServer
    {
        public NullServer() : base(port: 0) { }
        public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) { }
    }

    [Params(64 * 1024, 1024 * 1024)]
    public int PayloadSize;

    private byte[] _payload = null!;
    private RakNetSession _session = null!;

    [GlobalSetup]
    public void Setup()
    {
        _payload = new byte[PayloadSize];
        new Random(42).NextBytes(_payload);
        _session = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19144),
            Id = 1,
            Server = new NullServer(),
            MTU = 1400
        };
    }

    [Benchmark(Description = "Outbound fragmentation via real SendFrame pipeline")]
    public void Fragment_ViaSendFrame()
    {
        _session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = _payload
        }, RakNetSession.Priority.Immediate);
    }
}

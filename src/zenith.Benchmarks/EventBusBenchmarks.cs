using BenchmarkDotNet.Attributes;
using Zenith.Event;

namespace Zenith.Benchmarks;

[MemoryDiagnoser]
public class EventBusBenchmarks
{
    private EventBus _bus = null!;
    private readonly object _evt = new();

    [Params(0, 1, 8)]
    public int ListenerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _bus = new EventBus();
        for (var i = 0; i < ListenerCount; i++)
            _bus.Subscribe<object>(_ => { });
    }

    [Benchmark]
    public void Publish() => _bus.Publish(_evt);
}

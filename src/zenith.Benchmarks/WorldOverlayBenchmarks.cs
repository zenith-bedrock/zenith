using BenchmarkDotNet.Attributes;
using Zenith.World;

namespace Zenith.Benchmarks;

/// <summary>Tick authority RAM overlay: Set/Get + column scan cost vs overlay count.</summary>
[MemoryDiagnoser]
public class WorldOverlayBenchmarks
{
    private global::Zenith.World.World _world = null!;
    private int _hitX;
    private int _hitY;
    private int _hitZ;

    [Params(0, 1000, 10000)]
    public int OverlayCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Blocks.EnsureLoaded();
        _world = new global::Zenith.World.World(new InMemoryChunkStorage());

        // Pack into chunk (0,0) so GetOverlaysInColumn scales with OverlayCount.
        for (var i = 0; i < OverlayCount; i++)
        {
            var x = i % 16;
            var z = (i / 16) % 16;
            var y = Blocks.FlatSpawnY - 1 - ((i / 256) % 40);
            _world.SetBlock(x, y, z, Blocks.Stone);
            if (i == OverlayCount - 1)
            {
                _hitX = x;
                _hitY = y;
                _hitZ = z;
            }
        }

        if (OverlayCount == 0)
        {
            _hitX = 0;
            _hitY = Blocks.FlatSpawnY - 1;
            _hitZ = 0;
        }
    }

    [Benchmark]
    public void SetBlock()
    {
        // Mutate a fixed cell so count stays stable across iterations.
        _world.SetBlock(1, Blocks.FlatSpawnY - 2, 1, Blocks.Dirt);
    }

    [Benchmark]
    public int GetBlock() => _world.GetBlock(_hitX, _hitY, _hitZ);

    [Benchmark]
    public int GetOverlaysInColumn() => _world.GetOverlaysInColumn(0, 0).Count;
}

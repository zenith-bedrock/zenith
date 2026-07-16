using BenchmarkDotNet.Attributes;
using Zenith.World;

namespace Zenith.Benchmarks;

/// <summary>Static Blocks façade + ItemPalette Require — hot-path dict baseline (ADR §25).</summary>
[MemoryDiagnoser]
public class PaletteLookupBenchmarks
{
    private ItemPalette _items = null!;

    [GlobalSetup]
    public void Setup()
    {
        Blocks.EnsureLoaded();
        _items = ItemPaletteLoader.FromEmbeddedResource();
    }

    [Benchmark]
    public int BlocksStone() => Blocks.Stone;

    [Benchmark]
    public int BlocksChest() => Blocks.Chest;

    [Benchmark]
    public int BlocksAir() => Blocks.Air;

    [Benchmark]
    public short ItemStone() => _items.Require("minecraft:stone");

    [Benchmark]
    public short ItemChest() => _items.Require("minecraft:chest");

    [Benchmark]
    public short ItemOakLog() => _items.Require("minecraft:oak_log");
}

using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class OrePlacerTests
{
    public OrePlacerTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Ore_placement_is_deterministic()
    {
        const int seed = 42;
        var a = OverworldOrePlacer.TryReplaceHost(Blocks.Stone, 100, 24, -50, seed);
        var b = OverworldOrePlacer.TryReplaceHost(Blocks.Stone, 100, 24, -50, seed);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Ore_only_replaces_stone_or_deepslate_hosts()
    {
        const int seed = 7;
        Assert.Equal(0, OverworldOrePlacer.TryReplaceHost(Blocks.GrassBlock, 0, 64, 0, seed));
        Assert.Equal(0, OverworldOrePlacer.TryReplaceHost(Blocks.Dirt, 0, 63, 0, seed));
        Assert.Equal(0, OverworldOrePlacer.TryReplaceHost(Blocks.Air, 0, 50, 0, seed));
    }

    [Fact]
    public void Deepslate_band_uses_deepslate_ore_variants()
    {
        const int seed = 21;
        var noise = new NoiseTerrainProvider(seed);
        var foundDeepslateOre = false;
        for (var x = -48; x < 48 && !foundDeepslateOre; x++)
        {
            for (var z = -48; z < 48 && !foundDeepslateOre; z++)
            {
                for (var y = -32; y < 0; y++)
                {
                    var b = noise.SampleBaseBlock(x, y, z);
                    if (IsDeepslateOre(b))
                        foundDeepslateOre = true;
                }
            }
        }

        Assert.True(foundDeepslateOre);
    }

    [Fact]
    public void Noise_region_contains_common_ores()
    {
        const int seed = 21;
        var noise = new NoiseTerrainProvider(seed);
        var foundCoal = false;
        var foundIron = false;
        for (var x = -96; x < 96 && !(foundCoal && foundIron); x++)
        {
            for (var z = -96; z < 96 && !(foundCoal && foundIron); z++)
            {
                for (var y = Blocks.FlatMinY + 2; y <= 48; y++)
                {
                    var b = noise.SampleBaseBlock(x, y, z);
                    if (b == Blocks.CoalOre || b == Blocks.DeepslateCoalOre) foundCoal = true;
                    if (b == Blocks.IronOre || b == Blocks.DeepslateIronOre) foundIron = true;
                }
            }
        }

        Assert.True(foundCoal);
        Assert.True(foundIron);
    }

    [Fact]
    public void Column_build_matches_SampleBaseBlock_with_ores()
    {
        const int seed = 12;
        var noise = new NoiseTerrainProvider(seed);
        var world = new World.World(new InMemoryChunkStorage(), terrain: noise);
        for (var x = -4; x < 20; x++)
        {
            for (var z = -4; z < 20; z++)
            {
                for (var y = Blocks.FlatMinY; y <= 80; y++)
                {
                    if (y % 2 != 0) continue;
                    Assert.Equal(noise.SampleBaseBlock(x, y, z), world.GetBlock(x, y, z));
                }
            }
        }
    }

    [Fact]
    public void Column_ore_context_matches_legacy_cell_generation()
    {
        const int seed = 31415;
        const int chunkX = -3;
        const int chunkZ = 4;
        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        Span<OreCell> cells = stackalloc OreCell[OverworldOrePlacer.MaxColumnCellCount];
        var count = OverworldOrePlacer.FillColumnCells(
            baseX, baseZ, seed, cells,
            out var minCellX, out var minCellY, out var minCellZ,
            out var widthX, out var widthY, out var widthZ);
        var view = cells[..count];

        for (var x = baseX; x < baseX + 16; x++)
        for (var z = baseZ; z < baseZ + 16; z++)
        for (var y = Blocks.FlatMinY; y <= OverworldOrePlacer.ColumnMaxOreY; y += 2)
        {
            var deep = y < 0;
            var host = deep ? Blocks.Deepslate : Blocks.Stone;
            var legacy = OverworldOrePlacer.TryReplaceHost(host, x, y, z, seed);
            var planned = OverworldOrePlacer.TryReplaceHost(
                deep, x, y, z, view,
                minCellX, minCellY, minCellZ, widthX, widthY, widthZ);
            Assert.Equal(legacy, planned);
        }
    }

    /// <summary>
    /// Regression: the fast per-column cell grid only covers up to <c>ColumnMaxOreY</c>. Sampling a
    /// worldY above that (e.g. deep terrain under an unusually tall mountain/feature) must not index
    /// outside the precomputed grid — it should fall back to the always-safe per-point path instead
    /// of throwing, and must still agree with that path's answer.
    /// </summary>
    [Fact]
    public void SampleNoiseBlockAtSurfaceWithOre_falls_back_safely_above_ColumnMaxOreY()
    {
        const int seed = 2024;
        const int chunkX = 5;
        const int chunkZ = -2;
        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        Span<OreCell> cells = stackalloc OreCell[OverworldOrePlacer.MaxColumnCellCount];
        var count = OverworldOrePlacer.FillColumnCells(
            baseX, baseZ, seed, cells,
            out var minCellX, out var minCellY, out var minCellZ,
            out var widthX, out var widthY, out var widthZ);
        var view = cells[..count];

        var features = OverworldTerrainSampler.FeaturePlacementPlan.Build(chunkX, chunkZ, seed);
        var highSurface = 300; // still within [FlatMinY, 320] — worldY below must read as "underground"
        // Far corner of the chunk (max cx/cz cell) so a Y overshoot pushes the flattened cell index
        // past the end of the buffer instead of silently aliasing a neighboring column's cell.
        const int x = 5 * 16 + 15;
        const int z = -2 * 16 + 15;
        var y = OverworldOrePlacer.ColumnMaxOreY + 80; // above the grid's built range, still <= 320

        var viaFastPath = OverworldTerrainSampler.SampleNoiseBlockAtSurfaceWithOre(
            x, y, z, seed, highSurface, caves: null, OverworldBiomeKind.Plains, features,
            view, minCellX, minCellY, minCellZ, widthX, widthY, widthZ);
        var viaLegacyPointPath = OverworldTerrainSampler.SampleNoiseBlockAtSurface(
            x, y, z, seed, highSurface, caves: null, OverworldBiomeKind.Plains);

        Assert.Equal(viaLegacyPointPath, viaFastPath);
    }

    [Fact]
    public void Ores_never_replace_surface_layers()
    {
        const int seed = 99;
        var noise = new NoiseTerrainProvider(seed);
        for (var x = -32; x < 32; x++)
        {
            for (var z = -32; z < 32; z++)
            {
                var surface = OverworldTerrainSampler.SurfaceY(x, z, seed);
                Assert.NotEqual(Blocks.CoalOre, noise.SampleBaseBlock(x, surface, z));
                Assert.Equal(Blocks.GrassBlock, noise.SampleBaseBlock(x, surface, z));
                for (var y = surface - OverworldTerrainSampler.NoiseDirtDepth; y < surface; y++)
                    Assert.False(IsAnyOre(noise.SampleBaseBlock(x, y, z)));
            }
        }
    }

    private static bool IsAnyOre(int block) =>
        block == Blocks.CoalOre
        || block == Blocks.IronOre
        || block == Blocks.CopperOre
        || block == Blocks.GoldOre
        || block == Blocks.DiamondOre
        || block == Blocks.LapisOre
        || block == Blocks.RedstoneOre
        || IsDeepslateOre(block);

    private static bool IsDeepslateOre(int block) =>
        block == Blocks.DeepslateCoalOre
        || block == Blocks.DeepslateIronOre
        || block == Blocks.DeepslateCopperOre
        || block == Blocks.DeepslateGoldOre
        || block == Blocks.DeepslateDiamondOre
        || block == Blocks.DeepslateLapisOre
        || block == Blocks.DeepslateRedstoneOre;
}

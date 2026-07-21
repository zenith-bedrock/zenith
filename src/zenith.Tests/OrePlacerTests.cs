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

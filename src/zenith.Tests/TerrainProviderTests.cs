using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class TerrainProviderTests
{
    public TerrainProviderTests() => Blocks.EnsureLoaded();

    private sealed class MarkerTerrain : ITerrainProvider
    {
        public byte[] Payload { get; } = [8, 1, 2, 3, 4, 5];

        public TerrainColumn GetBaseColumn(int chunkX, int chunkZ)
        {
            _ = chunkX;
            _ = chunkZ;
            return new TerrainColumn(SubChunkCount: 1, Payload);
        }

        public int SampleBaseBlock(int x, int y, int z)
        {
            _ = x;
            _ = z;
            return y == 0 ? Blocks.Dirt : Blocks.Air;
        }

        public int SampleSpawnFeetY(int x, int z)
        {
            _ = x;
            _ = z;
            return 1;
        }
    }

    [Fact]
    public async Task GetOrCreateColumnAsync_miss_uses_injected_terrain_payload()
    {
        var marker = new MarkerTerrain();
        var world = new World.World(new InMemoryChunkStorage(), terrain: marker);

        var column = await world.GetOrCreateColumnAsync(12, -3);

        Assert.Equal(1, column.Base.SubChunkCount);
        Assert.Equal(marker.Payload, column.Base.ExtraPayload);
        Assert.Equal(12, column.Base.Coord.X);
        Assert.Equal(-3, column.Base.Coord.Z);
    }

    [Fact]
    public void GetBlock_samples_injected_terrain()
    {
        var world = new World.World(new InMemoryChunkStorage(), terrain: new MarkerTerrain());
        Assert.Equal(Blocks.Dirt, world.GetBlock(0, 0, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(0, 1, 0));
    }

    [Fact]
    public void WorldStorageKeys_round_trip_overlay_and_chest()
    {
        var ov = WorldStorageKeys.Overlay(1, -60, 2);
        Assert.True(WorldStorageKeys.TryParseOverlay(System.Text.Encoding.UTF8.GetString(ov), out var x, out var y, out var z));
        Assert.Equal((1, -60, 2), (x, y, z));

        var ct = WorldStorageKeys.Chest(-3, 10, 4);
        Assert.True(WorldStorageKeys.TryParseChest(System.Text.Encoding.UTF8.GetString(ct), out x, out y, out z));
        Assert.Equal((-3, 10, 4), (x, y, z));
    }

    [Fact]
    public void FlatTerrainProvider_matches_legacy_flat_sample()
    {
        var flat = FlatTerrainProvider.Instance;
        Assert.Equal(Blocks.Stone, flat.SampleBaseBlock(0, Blocks.FlatStoneTopY, 0));
        Assert.Equal(Blocks.GrassBlock, flat.SampleBaseBlock(0, Blocks.FlatGrassY, 0));
        Assert.Equal(Blocks.Air, flat.SampleBaseBlock(0, Blocks.FlatSpawnY, 0));
        var col = flat.GetBaseColumn(0, 0);
        Assert.True(col.SubChunkCount > 0);
        Assert.True(col.Payload.Length >= 3);
        Assert.Equal(8, col.Payload[0]);
    }

    [Fact]
    public void Noise_surface_is_deterministic_in_overworld_band()
    {
        var a = OverworldTerrainSampler.SurfaceY(42, -17, 99);
        var b = OverworldTerrainSampler.SurfaceY(42, -17, 99);
        Assert.Equal(a, b);
        Assert.InRange(a, Blocks.FlatMinY + 12, 120);
        Assert.NotEqual(
            OverworldTerrainSampler.SurfaceY(0, 0, 99),
            OverworldTerrainSampler.SurfaceY(80, 80, 99));
    }

    [Fact]
    public void Noise_GetBlock_matches_SampleBaseBlock_including_features()
    {
        const int seed = 7;
        var noise = new NoiseTerrainProvider(seed);
        var world = new World.World(new InMemoryChunkStorage(), terrain: noise);
        for (var x = -8; x < 24; x++)
        {
            for (var z = -8; z < 24; z++)
            {
                for (var y = Blocks.FlatMinY; y <= 96; y += 3)
                    Assert.Equal(noise.SampleBaseBlock(x, y, z), world.GetBlock(x, y, z));
            }
        }
    }

    [Fact]
    public void Noise_has_bedrock_floor_and_deepslate_band()
    {
        var noise = new NoiseTerrainProvider(3);
        Assert.Equal(Blocks.Bedrock, noise.SampleBaseBlock(5, Blocks.FlatMinY, 5));
        // Deepslate below 0 when not carved — search a few columns.
        var foundDeep = false;
        for (var x = 0; x < 32 && !foundDeep; x++)
        {
            for (var z = 0; z < 32 && !foundDeep; z++)
            {
                if (noise.SampleBaseBlock(x, -8, z) == Blocks.Deepslate)
                    foundDeep = true;
            }
        }

        Assert.True(foundDeep);
    }

    [Fact]
    public void Noise_emits_trees_in_a_region()
    {
        var noise = new NoiseTerrainProvider(21);
        var foundLog = false;
        var foundLeaves = false;
        for (var x = -64; x < 64 && !(foundLog && foundLeaves); x++)
        {
            for (var z = -64; z < 64 && !(foundLog && foundLeaves); z++)
            {
                var surface = OverworldTerrainSampler.SurfaceY(x, z, 21);
                for (var y = surface + 1; y <= surface + 8; y++)
                {
                    var b = noise.SampleBaseBlock(x, y, z);
                    if (b == Blocks.OakLog) foundLog = true;
                    if (b == Blocks.OakLeaves) foundLeaves = true;
                }
            }
        }

        Assert.True(foundLog);
        Assert.True(foundLeaves);
    }

    [Fact]
    public void Noise_spawn_feet_is_clear_air_above_terrain()
    {
        var noise = new NoiseTerrainProvider(12);
        var world = new World.World(new InMemoryChunkStorage(), terrain: noise);
        var feet = world.SampleSpawnFeetY(0, 0);
        Assert.True(feet > OverworldTerrainSampler.SeaLevel);
        Assert.Equal(Blocks.Air, world.GetBlock(0, feet, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(0, feet + 1, 0));
    }

    [Fact]
    public void Flat_spawn_feet_matches_legacy()
    {
        Assert.Equal(Blocks.FlatSpawnY, FlatTerrainProvider.Instance.SampleSpawnFeetY(0, 0));
        Assert.Equal(Blocks.FlatSpawnY, new World.World(new InMemoryChunkStorage()).SampleSpawnFeetY(99, -3));
    }

    [Fact]
    public void Noise_columns_span_multiple_subchunks()
    {
        var noise = new NoiseTerrainProvider(12);
        var col = noise.GetBaseColumn(0, 0);
        // Surface ~64 ⇒ several sections above Y -64.
        Assert.True(col.SubChunkCount >= 6);
        Assert.Equal(8, col.Payload[0]);
        var other = noise.GetBaseColumn(3, -2);
        Assert.NotEqual(col.Payload, other.Payload);
    }

    [Fact]
    public async Task Noise_spawn_radius_4_loads_within_join_budget()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
        var world = new World.World(new InMemoryChunkStorage(), terrain: new NoiseTerrainProvider(42));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var columns = await world.GetRadiusAsync(0, 0, radius: 4);
        sw.Stop();
        Assert.Equal(81, columns.Count);
        // PreSpawn must finish before typical client/RakNet timeout. Parallel gen; allow suite contention.
        Assert.True(sw.ElapsedMilliseconds < 90_000, $"81 noise columns took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void TerrainProviders_Create_selects_mode()
    {
        Assert.IsType<FlatTerrainProvider>(TerrainProviders.Create(TerrainProviders.ModeFlat, 1));
        Assert.IsType<NoiseTerrainProvider>(TerrainProviders.Create(TerrainProviders.ModeNoise, 1));
        Assert.Throws<InvalidOperationException>(() => TerrainProviders.Create("caves", 1));
    }
}

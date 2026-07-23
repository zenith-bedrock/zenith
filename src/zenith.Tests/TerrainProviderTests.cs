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
        // PreSpawn must finish before typical client/RakNet timeout (ADR §69).
        Assert.True(sw.ElapsedMilliseconds < 15_000, $"81 noise columns took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void TerrainProviders_Create_selects_mode()
    {
        Assert.IsType<FlatTerrainProvider>(TerrainProviders.Create(TerrainProviders.ModeFlat, 1));
        Assert.IsType<NoiseTerrainProvider>(TerrainProviders.Create(TerrainProviders.ModeNoise, 1));
        Assert.Throws<InvalidOperationException>(() => TerrainProviders.Create("caves", 1));
    }

    [Fact]
    public void Noise_classic_flat_Y_is_solid_spawn_feet_is_surface()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
        var n = new NoiseTerrainProvider(1);
        var t = n.GetBaseColumn(0, 0);
        Assert.True(t.Payload.Length > 0);
        Assert.NotEqual(Blocks.Air, n.SampleBaseBlock(0, Blocks.FlatSpawnY, 0));
        var spawnY = n.SampleSpawnFeetY(0, 0);
        Assert.True(spawnY >= OverworldTerrainSampler.SeaLevel,
            $"noise spawn Y={spawnY} payloadBytes={t.Payload.Length} sub={t.SubChunkCount}");
        Assert.Equal(Blocks.Air, n.SampleBaseBlock(0, spawnY, 0));
        Assert.Equal(Blocks.Air, n.SampleBaseBlock(0, spawnY + 1, 0));
    }

    [Fact]
    public void World_TryHealSpawnFeet_lifts_buried_flat_pose_on_noise()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
        var world = new World.World(new InMemoryChunkStorage(), terrain: new NoiseTerrainProvider(1));
        var player = new Player.Player("healee", null!, runtimeId: 1, Guid.NewGuid())
        {
            PositionX = 0f,
            PositionY = Blocks.FlatSpawnY,
            PositionZ = 0f
        };

        Assert.False(world.IsSpawnFeetClear(0, Blocks.FlatSpawnY, 0));
        Assert.True(world.TryHealSpawnFeet(player));
        Assert.True(player.PositionY >= OverworldTerrainSampler.SeaLevel);
        Assert.True(world.IsSpawnFeetClear(
            (int)MathF.Floor(player.PositionX),
            (int)MathF.Floor(player.PositionY),
            (int)MathF.Floor(player.PositionZ)));
        Assert.False(world.TryHealSpawnFeet(player)); // already clear
    }

    [Fact]
    public void Noise_surface_adjacent_steps_are_bounded()
    {
        const int seed = 99;
        var maxStep = 0;
        for (var x = -64; x < 64; x++)
        {
            for (var z = -64; z < 64; z++)
            {
                var y = OverworldTerrainSampler.SurfaceY(x, z, seed);
                maxStep = Math.Max(maxStep, Math.Abs(y - OverworldTerrainSampler.SurfaceY(x + 1, z, seed)));
                maxStep = Math.Max(maxStep, Math.Abs(y - OverworldTerrainSampler.SurfaceY(x, z + 1, seed)));
            }
        }

        Assert.True(
            maxStep <= OverworldTerrainSampler.MaxAdjacentSurfaceStep,
            $"adjacent surface step {maxStep} exceeds {OverworldTerrainSampler.MaxAdjacentSurfaceStep} (Simplex height)");
    }

    [Fact]
    public void Noise_column_includes_cross_chunk_tree_canopy_y()
    {
        const int seed = 21;
        // Find a trunk near a chunk edge whose canopy Y exceeds the neighbor's local surface+headroom.
        for (var x = -128; x < 128; x++)
        {
            for (var z = -128; z < 128; z++)
            {
                var surface = OverworldTerrainSampler.SurfaceY(x, z, seed);
                if (surface < OverworldTerrainSampler.SeaLevel) continue;
                if (new NoiseTerrainProvider(seed).SampleBaseBlock(x, surface + 1, z) != Blocks.OakLog)
                    continue;

                // Prefer trunks on the +X edge of their chunk so canopy spills into +chunk.
                if ((x & 15) < 13) continue;

                var canopyTop = -1;
                for (var y = surface + 1; y <= surface + 8; y++)
                {
                    if (OverworldTerrainSampler.SampleNoiseBlock(x, y, z, seed) == Blocks.OakLog
                        || OverworldTerrainSampler.SampleNoiseBlock(x, y, z, seed) == Blocks.OakLeaves)
                        canopyTop = y;
                }

                if (canopyTop < 0) continue;

                var neighborChunkX = (x >> 4) + 1;
                var neighborChunkZ = z >> 4;
                var leafX = x + 1;
                if ((leafX >> 4) != neighborChunkX) continue;

                var leafBlock = OverworldTerrainSampler.SampleNoiseBlock(leafX, canopyTop, z, seed);
                if (leafBlock != Blocks.OakLeaves && leafBlock != Blocks.OakLog)
                    continue;

                var featureMax = OverworldTerrainSampler.MaxTreeCanopyYAffectingChunk(
                    neighborChunkX, neighborChunkZ, seed);
                Assert.True(featureMax >= canopyTop,
                    $"neighbor featureMaxY {featureMax} < canopy {canopyTop} at trunk ({x},{z})");

                var caves = OverworldCaveContext.ForColumn(neighborChunkX, neighborChunkZ, seed);
                var col = ChunkPayloads.BuildNoiseOverworldColumn(neighborChunkX, neighborChunkZ, seed, caves);
                // Section index must reach canopyTop (OverworldMinSubChunkIndex = -4).
                var sectionForCanopy = (canopyTop >> 4) - (-4);
                Assert.True(
                    col.SubChunkCount > sectionForCanopy,
                    $"neighbor SubChunkCount {col.SubChunkCount} must cover canopy section {sectionForCanopy}");
                return;
            }
        }

        Assert.Fail("no edge trunk with cross-chunk canopy found for seed 21");
    }
}

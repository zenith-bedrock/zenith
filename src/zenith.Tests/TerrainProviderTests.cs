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
}

using Zenith.Packets;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class DimensionIdAlignmentTests
{
    [Fact]
    public void Dimension_overworld_wire_matches_packets()
    {
        Assert.Equal(DimensionId.Overworld, Dimension.OverworldWireId);
        var dim = Dimension.CreateOverworld(FlatTerrainProvider.Instance);
        Assert.Equal("overworld", dim.Identifier);
        Assert.Equal(DimensionId.Overworld, dim.WireId);
        Assert.Same(FlatTerrainProvider.Instance, dim.Terrain);
    }

    [Fact]
    public void World_exposes_overworld_dimension_on_columns()
    {
        Blocks.EnsureLoaded();
        var world = new World.World(new InMemoryChunkStorage(), terrain: FlatTerrainProvider.Instance);
        Assert.Equal(DimensionId.Overworld, world.Overworld.WireId);
        var col = world.GetOrCreateColumnAsync(0, 0).AsTask().GetAwaiter().GetResult();
        Assert.Equal(DimensionId.Overworld, col.Base.DimensionId);
    }
}

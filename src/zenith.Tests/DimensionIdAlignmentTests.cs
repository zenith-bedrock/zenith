using Zenith.Packets;
using Xunit;

namespace Zenith.Tests;

public class DimensionIdAlignmentTests
{
    [Fact]
    public void World_overworld_matches_packets_dimension_id()
    {
        Assert.Equal(DimensionId.Overworld, global::Zenith.World.World.OverworldDimensionId);
    }
}

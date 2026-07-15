using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public class ChestFacingTests
{
    public ChestFacingTests()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
    }

    [Theory]
    [InlineData(0f, Blocks.CardinalNorth)]   // look south → front north
    [InlineData(10f, Blocks.CardinalNorth)]
    [InlineData(90f, Blocks.CardinalEast)]   // look west → front east
    [InlineData(180f, Blocks.CardinalSouth)] // look north → front south
    [InlineData(270f, Blocks.CardinalWest)]  // look east → front west
    [InlineData(-10f, Blocks.CardinalNorth)]
    [InlineData(360f, Blocks.CardinalNorth)]
    public void FromYaw_opposite_of_look(float yaw, string expectedCardinal)
    {
        Assert.Equal(expectedCardinal, ChestFacing.FromYaw(yaw));
        Assert.Equal(Blocks.ChestForFacing(expectedCardinal), ChestFacing.RuntimeIdFromYaw(yaw));
    }

    [Fact]
    public void RuntimeIdFromYaw_not_always_item_form_south()
    {
        var eastFacing = ChestFacing.RuntimeIdFromYaw(90f);
        Assert.NotEqual(Blocks.Chest, eastFacing);
        Assert.True(Blocks.IsChest(eastFacing));
    }
}

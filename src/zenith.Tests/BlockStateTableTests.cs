using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public class BlockStateTableTests
{
    private static BlockStateTable States => Blocks.Catalog.States;

    [Fact]
    public void Every_state_of_the_palette_belongs_to_exactly_one_family_ordinal()
    {
        var palette = Blocks.Palette;
        var seen = 0;
        foreach (var (id, state) in palette.AllStates)
        {
            var family = States.FamilyOf(id);
            Assert.NotNull(family);
            Assert.Equal(state.Count, family.Properties.Length);
            seen++;
        }
        Assert.Equal(palette.StateCount, seen);
    }

    [Fact]
    public void Typed_reads_agree_with_the_palette_strings_for_every_state()
    {
        var checkedStates = 0;
        foreach (var (id, state) in Blocks.Palette.AllStates)
        {
            if (state.TryGet("open_bit", out var open))
            {
                Assert.True(States.TryGet(id, StateKeys.Open, out var bit));
                Assert.Equal(open == "1", bit);
                checkedStates++;
            }
            if (state.TryGet("minecraft:vertical_half", out var half))
            {
                Assert.True(States.TryGet(id, StateKeys.VerticalHalf, out VerticalHalf typed));
                Assert.Equal(half == "top" ? VerticalHalf.Top : VerticalHalf.Bottom, typed);
                checkedStates++;
            }
            if (state.TryGet("weirdo_direction", out var facing))
            {
                Assert.True(States.TryGet(id, StateKeys.StairFacing, out Cardinal dir));
                Assert.Equal(facing, dir switch { Cardinal.East => "0", Cardinal.West => "1", Cardinal.South => "2", _ => "3" });
                checkedStates++;
            }
        }
        Assert.True(checkedStates > 1000, $"expected broad coverage, got {checkedStates}");
    }

    [Fact]
    public void Setting_a_property_changes_only_that_property_and_round_trips()
    {
        var wall = Blocks.Palette.Require("minecraft:cobblestone_wall");
        var east = StateKeys.WallConnections[(int)Cardinal.East];
        var edited = wall;
        Assert.True(States.TrySet(ref edited, east, WallConnection.Tall));
        Assert.NotEqual(wall, edited);
        Assert.True(States.TryGet(edited, east, out WallConnection read) && read == WallConnection.Tall);

        var back = edited;
        Assert.True(States.TrySet(ref back, east, States.TryGet(wall, east, out WallConnection original) ? original : default));
        Assert.Equal(wall, back);

        Assert.True(Blocks.Palette.TryGetState(wall, out var before));
        Assert.True(Blocks.Palette.TryGetState(edited, out var after));
        var differing = before.Entries.Zip(after.Entries).Count(pair => pair.First.Value != pair.Second.Value);
        Assert.Equal(1, differing);
    }

    [Fact]
    public void Editing_a_property_the_block_lacks_leaves_the_id_alone()
    {
        var stone = Blocks.Stone;
        var id = stone;
        Assert.False(States.TrySet(ref id, StateKeys.Open, true));
        Assert.Equal(stone, id);
        Assert.False(States.TryGet(stone, StateKeys.Open, out _));
    }

    [Fact]
    public void Look_direction_follows_the_bedrock_yaw_convention()
    {
        Assert.Equal(Cardinal.South, Directions.LookFromYaw(0f));
        Assert.Equal(Cardinal.West, Directions.LookFromYaw(90f));
        Assert.Equal(Cardinal.North, Directions.LookFromYaw(180f));
        Assert.Equal(Cardinal.East, Directions.LookFromYaw(270f));
        Assert.Equal(Cardinal.East, Directions.LookFromYaw(-90f));
        Assert.Equal(Cardinal.South, Directions.LookFromYaw(float.NaN));
        Assert.Equal(Cardinal.North, Cardinal.South.Opposite());
        Assert.Equal(Cardinal.East, Cardinal.North.Clockwise());
    }
}

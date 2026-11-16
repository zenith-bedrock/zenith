using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public class BlockStateTests
{
    private static readonly BlockPalette Palette = BlockPaletteLoader.FromEmbeddedResource();

    [Fact]
    public void Every_runtime_id_has_a_state_and_round_trips_through_exact_lookup()
    {
        Assert.Equal(Palette.ReverseCount, Palette.StateCount);
        var checkedIds = 0;
        foreach (var name in new[] { "minecraft:oak_stairs", "minecraft:oak_log", "minecraft:wooden_door", "minecraft:oak_slab", "minecraft:stone" })
        {
            Assert.True(Palette.TryGet(name, out var id));
            Assert.True(Palette.TryGetState(id, out var state));
            Assert.True(Palette.TryGetExact(name, state, out var again));
            Assert.Equal(id, again);
            checkedIds++;
        }
        Assert.Equal(5, checkedIds);
    }

    [Fact]
    public void Resolve_applies_overrides_to_the_default_state_and_rejects_unknown_properties()
    {
        Assert.True(Palette.TryResolve("minecraft:oak_stairs",
            new Dictionary<string, string> { ["weirdo_direction"] = "2", ["upside_down_bit"] = "1" }, out var id));
        Assert.True(Palette.TryGetState(id, out var state));
        Assert.Equal("2", state.Get("weirdo_direction"));
        Assert.Equal("1", state.Get("upside_down_bit"));

        Assert.False(Palette.TryResolve("minecraft:oak_stairs", new Dictionary<string, string> { ["nonsense"] = "1" }, out _));
        Assert.False(Palette.TryResolve("minecraft:oak_stairs", new Dictionary<string, string> { ["weirdo_direction"] = "99" }, out _));
        Assert.False(Palette.TryResolve("minecraft:not_a_block", new Dictionary<string, string>(), out _));
    }

    [Fact]
    public void Properties_list_legal_values()
    {
        var stairs = Palette.PropertiesOf("minecraft:oak_stairs");
        Assert.NotNull(stairs);
        Assert.Equal(4, stairs!["weirdo_direction"].Count);
        Assert.Null(Palette.PropertiesOf("minecraft:stone"));
    }
}

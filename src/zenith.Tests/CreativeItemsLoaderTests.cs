using System.Linq;
using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Regression for ADR §140's core claim: a creative item's `block_state_b64` embeds the exact same
/// `network_id` <c>block_palette.nbt</c> already uses as Zenith's own block runtime id — verified
/// byte-for-byte against `minecraft:oak_planks` in both files before this loader was written.
/// </summary>
public class CreativeItemsLoaderTests
{
    public CreativeItemsLoaderTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Loads_real_group_and_item_counts()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var data = CreativeItemsLoader.FromEmbeddedResource(palette);

        Assert.True(data.Groups.Count > 100);
        Assert.True(data.Items.Count > 1000);
    }

    [Fact]
    public void Block_form_item_resolves_to_the_same_runtime_id_Blocks_already_uses()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var data = CreativeItemsLoader.FromEmbeddedResource(palette);

        var oakPlanks = data.Items.Single(i => i.Id.IsBlock && Blocks.TryGetName(i.Id.Value, out var name) && name == "minecraft:oak_planks");

        Assert.Equal(Blocks.OakPlanks, oakPlanks.Id.Value);
    }

    [Fact]
    public void Pure_item_form_resolves_via_the_item_palette()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var data = CreativeItemsLoader.FromEmbeddedResource(palette);

        var stick = data.Items.Single(i => i.Id.IsItem && palette.TryGetName(i.Id.Value, out var name) && name == "minecraft:stick");
        Assert.Equal(palette.Require("minecraft:stick"), stick.Id.Value);
    }

    [Fact]
    public void Every_item_group_index_is_in_range()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var data = CreativeItemsLoader.FromEmbeddedResource(palette);

        Assert.All(data.Items, item => Assert.InRange(item.GroupIndex, 0, data.Groups.Count - 1));
    }

    [Fact]
    public void Groups_have_real_names_and_resolved_icons()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var data = CreativeItemsLoader.FromEmbeddedResource(palette);

        var planks = Assert.Single(data.Groups, g => g.Name == "itemGroup.name.planks");
        Assert.False(planks.Icon.IsEmpty);
    }

    /// <summary>Real vanilla data: some groups use the "minecraft:empty" icon sentinel (client falls
    /// back to the group's first item) instead of a real icon — must resolve without throwing.</summary>
    [Fact]
    public void Groups_with_the_empty_icon_sentinel_resolve_to_an_empty_stack_id()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var data = CreativeItemsLoader.FromEmbeddedResource(palette);

        Assert.Contains(data.Groups, g => g.Icon.IsEmpty);
    }
}

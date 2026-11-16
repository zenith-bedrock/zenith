using System.Reflection;
using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Survival;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>A misspelt item name in a table or constant would otherwise match nothing and fail silently.</summary>
public sealed class ItemNamesTests
{
    private static ItemPalette Palette() => new IntentTestFixture().Context.ItemPalette;

    [Fact]
    public void Every_named_constant_is_an_item_in_the_palette()
    {
        var palette = Palette();
        var names = typeof(ItemNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name != nameof(ItemNames.Namespace))
            .Select(f => (string)f.GetRawConstantValue()!);

        var unknown = names.Where(n => !palette.TryGet(n, out _)).ToArray();
        Assert.True(unknown.Length == 0, "not in the item palette: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Every_food_and_armour_name_is_an_item_in_the_palette()
    {
        var palette = Palette();
        var unknown = FoodItems.Names.Concat(ArmorItems.Names).Where(n => !palette.TryGet(n, out _)).ToArray();
        Assert.True(unknown.Length == 0, "not in the item palette: " + string.Join(", ", unknown));
    }
}

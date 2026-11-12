using System.Text.Json.Nodes;
using Xunit;

namespace Zenith.BedrockData.Tests;

public sealed class DurabilityTests
{
    private static JsonArray Palette(params (string Name, int Id)[] items) =>
        new(items.Select(i => (JsonNode)new JsonObject { ["name"] = i.Name, ["id"] = i.Id }).ToArray());

    private static JsonNode Source(string name, int id, int? max) =>
        max is { } m
            ? new JsonObject { ["name"] = name, ["id"] = id, ["maxDurability"] = m }
            : new JsonObject { ["name"] = name, ["id"] = id };

    [Fact]
    public void Only_durable_items_present_in_the_palette_with_the_same_id_are_kept()
    {
        var result = Durability.Build(
            new JsonArray(Source("iron_pickaxe", 299, 250), Source("stick", 10, null), Source("future_tool", 900, 5), Source("moved_tool", 5, 7)),
            Palette(("minecraft:iron_pickaxe", 299), ("minecraft:stick", 10), ("minecraft:moved_tool", 6)));

        Assert.Equal(new[] { ("minecraft:iron_pickaxe", 250) }, result.Items.Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(["minecraft:future_tool"], result.Omitted);
        Assert.Equal(["minecraft:moved_tool"], result.IdSkewed);
    }

    [Fact]
    public void A_non_positive_or_duplicated_entry_fails_the_build()
    {
        var palette = Palette(("minecraft:a", 1));
        Assert.Throws<InvalidDataException>(() => Durability.Build(new JsonArray(Source("a", 1, 0)), palette));
        Assert.Throws<InvalidDataException>(() => Durability.Build(new JsonArray(Source("a", 1, 5), Source("a", 1, 5)), palette));
    }
}

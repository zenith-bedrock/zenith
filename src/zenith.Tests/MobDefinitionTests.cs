using System.Text;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>The species data file: every species a system uses exists, its loot is a real item, and a bad entry fails loudly.</summary>
public class MobDefinitionTests
{
    private static Stream Json(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Every_species_the_systems_use_is_defined()
    {
        var names = MobDefinitions.All.Select(d => d.Name).ToHashSet();

        Assert.Superset(
            new HashSet<string> { "Zombie", "Cow", "Creeper", "Spider", "Skeleton", "Golem", "Minecart" }, names);
        Assert.Equal(20f, MobDefinitions.Zombie.MaxHealth);
        Assert.Equal("minecraft:rotten_flesh", MobDefinitions.Zombie.LootItemName);
        Assert.Equal(25, MobDefinitions.Golem.KillExperience);
    }

    [Fact]
    public void Every_loot_item_is_in_the_item_palette()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();

        foreach (var definition in MobDefinitions.All)
            Assert.True(palette.TryGet(definition.LootItemName, out _), $"{definition.Name} drops unknown item {definition.LootItemName}.");
    }

    [Fact]
    public void An_unknown_species_name_is_an_error_not_a_default()
    {
        Assert.Throws<KeyNotFoundException>(() => MobDefinitions.Get("Dragon"));
    }

    [Theory]
    [InlineData("""{"mobs":[{"name":"Zombie","maxHealth":0,"lootItem":"x","killExperience":1}]}""")]
    [InlineData("""{"mobs":[{"name":"Zombie","maxHealth":5,"lootItem":"","killExperience":1}]}""")]
    [InlineData("""{"mobs":[{"name":"Zombie","maxHealth":5,"lootItem":"x","killExperience":-1}]}""")]
    [InlineData("""{"mobs":[{"name":"","maxHealth":5,"lootItem":"x","killExperience":1}]}""")]
    [InlineData("""{"mobs":[{"name":"Zombie","maxHealth":5,"lootItem":"x","killExperience":1},{"name":"Zombie","maxHealth":6,"lootItem":"x","killExperience":1}]}""")]
    [InlineData("""{"mobs":[{"name":"Dragon","maxHealth":5,"lootItem":"x","killExperience":1}]}""")]
    public void A_bad_entry_fails_to_load(string json)
    {
        Assert.Throws<InvalidDataException>(() => MobDefinitions.Parse(Json(json)));
    }
}

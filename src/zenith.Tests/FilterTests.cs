using System.Text;
using System.Text.Json;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Filters;
using Zenith.Rulebook;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 005 phase C: the BDS-style filter language (grammar, load-time validation, the built-in tests) and the data that uses it.</summary>
public sealed class FilterTests
{
    public FilterTests() => Blocks.EnsureLoaded();

    private static Filter Parse(string json, FilterRegistry? registry = null)
    {
        using var doc = JsonDocument.Parse(json);
        return new FilterParser(registry ?? FilterRegistry.Default).Parse(doc.RootElement.Clone(), "test");
    }

    private static async Task<IntentTestFixture> Stage()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, 199, z, Blocks.GrassBlock);
                for (var y = 200; y < 212; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        return fx;
    }

    private static FilterContext At(IntentTestFixture fx, int time) => new(fx.World, 8, 200, 8, time);

    // ---- grammar --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_list_is_all_of_and_the_groups_nest()
    {
        var fx = await Stage();
        var night = At(fx, 18000);
        Assert.True(Parse("""[ { "test": "hourly_clock_time", "operator": ">=", "value": 13000 }, { "test": "is_raining", "value": false } ]""").Matches(night));
        Assert.False(Parse("""[ { "test": "hourly_clock_time", "operator": "<", "value": 13000 }, { "test": "is_raining", "value": false } ]""").Matches(night));
        Assert.True(Parse("""{ "any_of": [ { "test": "is_raining" }, { "test": "hourly_clock_time", "value": 18000 } ] }""").Matches(night));
        Assert.False(Parse("""{ "none_of": [ { "test": "hourly_clock_time", "value": 18000 } ] }""").Matches(night));
        Assert.True(Parse("""{ "all_of": [ { "any_of": [ { "test": "is_raining" }, { "none_of": [ { "test": "is_raining" } ] } ] } ] }""").Matches(night));
    }

    [Fact]
    public async Task A_bool_test_defaults_to_true_and_the_number_operators_compare()
    {
        var fx = await Stage();
        Assert.False(Parse("""{ "test": "is_raining" }""").Matches(At(fx, 0)));
        fx.World.Weather.Set(1, 1000);
        Assert.True(Parse("""{ "test": "is_raining" }""").Matches(At(fx, 0)));
        Assert.True(Parse("""{ "test": "weather", "value": "rain" }""").Matches(At(fx, 0)));
        Assert.True(Parse("""{ "test": "weather", "operator": "not", "value": "clear" }""").Matches(At(fx, 0)));

        foreach (var (op, expected) in new[] { ("<", false), ("<=", true), (">", false), (">=", true), ("==", true), ("!=", false) })
            Assert.Equal(expected, Parse($$"""{ "test": "hourly_clock_time", "operator": "{{op}}", "value": 6000 }""").Matches(At(fx, 6000)));
    }

    [Theory]
    [InlineData("""{ "test": "nope" }""", "unknown test 'nope'")]
    [InlineData("""{ "test": "is_raining", "value": "yes" }""", "true or false")]
    [InlineData("""{ "test": "is_raining", "operator": "<" }""", "equals or not")]
    [InlineData("""{ "test": "light_level", "value": "seven" }""", "takes a number")]
    [InlineData("""{ "test": "light_level", "domain": "moon", "value": 7 }""", "no domain 'moon'")]
    [InlineData("""{ "test": "is_raining", "domain": "any" }""", "takes none")]
    [InlineData("""{ "test": "is_difficulty", "value": "brutal" }""", "does not know the value 'brutal'")]
    [InlineData("""{ "test": "weather", "value": "hail" }""", "does not know the value 'hail'")]
    [InlineData("""{ "test": "is_raining", "subject": "other" }""", "subject 'other'")]
    [InlineData("""{ "test": "is_raining", "colour": "red" }""", "unknown key 'colour'")]
    [InlineData("""{ "all_of": [] }""", "no filters")]
    [InlineData("""{ "all_of": [ { "test": "is_raining" } ], "any_of": [ { "test": "is_raining" } ] }""", "one group")]
    [InlineData("""{ "all_of": { "test": "is_raining" } }""", "list of filters")]
    [InlineData("""{ "both": [] }""", "unknown key 'both'")]
    [InlineData("""{ }""", "empty filter")]
    [InlineData("""7""", "object or a list")]
    public void A_filter_that_cannot_work_fails_at_load_with_its_path(string json, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Parse(json));
        Assert.StartsWith("test", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void An_error_inside_a_group_names_where_it_is()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Parse("""{ "all_of": [ { "test": "is_raining" }, { "any_of": [ { "test": "nope" } ] } ] }"""));
        Assert.Contains("test.all_of[1].any_of[0]", ex.Message);
    }

    [Fact]
    public async Task A_registry_is_an_instance_so_a_new_test_is_one_line()
    {
        var fx = await Stage();
        var registry = FilterRegistry.CreateDefault().AddNumber("height", (c, _) => c.Y);
        Assert.True(Parse("""{ "test": "height", "operator": ">=", "value": 200 }""", registry).Matches(At(fx, 0)));
        Assert.Throws<InvalidDataException>(() => Parse("""{ "test": "height", "value": 200 }"""));
        Assert.Throws<InvalidOperationException>(() => registry.AddNumber("height", (c, _) => c.Y));
    }

    // ---- the built-in tests ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Light_level_ignores_the_weather_unless_the_domain_asks_for_it()
    {
        var fx = await Stage();
        fx.World.Weather.Set(2, 1000);
        var plain = (double)AtNoon(fx, "");
        var adjusted = (double)AtNoon(fx, ", \"domain\": \"adjust_for_weather\"");
        Assert.Equal(15, plain);
        Assert.True(adjusted < plain, "a thunderstorm dims the sky for the monsters' rule only");

        int AtNoon(IntentTestFixture f, string domain)
        {
            for (var level = 15; level >= 0; level--)
                if (Parse($$"""{ "test": "light_level"{{domain}}, "operator": ">=", "value": {{level}} }""").Matches(At(f, 6000))) return level;
            return -1;
        }
    }

    [Fact]
    public async Task Is_block_looks_at_the_cell_below_above_or_the_cell_itself()
    {
        var fx = await Stage();
        var c = At(fx, 0);
        Assert.True(Parse("""{ "test": "is_block", "domain": "down", "value": "minecraft:grass_block" }""").Matches(c));
        Assert.False(Parse("""{ "test": "is_block", "domain": "up", "value": "minecraft:grass_block" }""").Matches(c));
        Assert.True(Parse("""{ "test": "is_block", "operator": "!=", "value": "minecraft:grass_block" }""").Matches(c));
    }

    [Fact]
    public async Task Biome_tags_and_difficulty_come_from_the_world()
    {
        var fx = await Stage();
        var c = At(fx, 0);
        Assert.True(Parse("""{ "test": "has_biome_tag", "value": "overworld" }""").Matches(c));
        Assert.False(Parse("""{ "test": "has_biome_tag", "value": "ocean" }""").Matches(c));
        Assert.True(Parse("""{ "test": "is_difficulty", "value": "normal" }""").Matches(c));
        fx.World.Difficulty.Set(Difficulty.Hard);
        Assert.True(Parse("""{ "test": "is_difficulty", "value": "hard" }""").Matches(c));
        Assert.False(Parse("""{ "test": "is_difficulty", "value": "normal" }""").Matches(c));
    }

    [Fact]
    public async Task The_sun_rule_wants_day_dry_weather_and_an_open_sky()
    {
        var fx = await Stage();
        var rule = EntityRules.Default.BurnsInDaylight;
        Assert.True(rule.Matches(At(fx, 2000)));
        Assert.False(rule.Matches(At(fx, 18000)));
        fx.World.Weather.Set(1, 1000);
        Assert.False(rule.Matches(At(fx, 2000)));
        fx.World.Weather.Set(0, 1000);
        fx.World.SetBlock(8, 205, 8, Blocks.Stone);
        Assert.False(rule.Matches(At(fx, 2000)));
    }

    // ---- the catalog and the shipped data -------------------------------------------------------------------------

    [Fact]
    public void Every_biome_the_generator_makes_has_tags_and_a_climate()
    {
        var catalog = BiomeCatalog.Default;
        foreach (var kind in Enum.GetValues<OverworldBiomeKind>())
        {
            var info = catalog.Of(kind);
            Assert.Contains("overworld", info.Tags);
            Assert.InRange(info.Downfall, 0f, 1f);
        }

        Assert.Contains("animal", catalog.Of(OverworldBiomeKind.Plains).Tags);
        Assert.DoesNotContain("animal", catalog.Of(OverworldBiomeKind.Desert).Tags);
        Assert.Contains("ocean", catalog.Of(OverworldBiomeKind.Ocean).Tags);
        Assert.Equal(2.0f, catalog.Of(OverworldBiomeKind.Desert).Temperature);
    }

    [Fact]
    public void A_catalog_missing_a_biome_kind_fails_at_load()
    {
        const string json = """{ "biomes": [ { "kind": "Plains", "id": "plains", "tags": [], "temperature": 0.8, "downfall": 0.4, "snowAccumulation": [0, 0.125] } ] }""";
        var ex = Assert.Throws<InvalidDataException>(() => BiomeCatalog.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json))));
        Assert.Contains("biome_tags.json", ex.Message);
        Assert.Contains("has no entry", ex.Message);
    }

    [Fact]
    public void The_shipped_rules_load()
    {
        Assert.NotEmpty(SpawnTable.Default.Entries);
        Assert.NotNull(EntityRules.Default.BurnsInDaylight);
    }
}

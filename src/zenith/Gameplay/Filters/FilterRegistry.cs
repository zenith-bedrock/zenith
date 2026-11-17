using Zenith.Rulebook;
using Zenith.World;

namespace Zenith.Gameplay.Filters;

enum FilterValueKind { Bool, Number, Text }

/// <summary>What a filter test is: its name, the kind of value it compares, the domains it accepts and how to read the fact from a context.</summary>
sealed class FilterTestDefinition
{
    public required string Name { get; init; }
    public required FilterValueKind Kind { get; init; }
    public IReadOnlySet<string> Domains { get; init; } = new HashSet<string>();
    public Func<FilterContext, string?, bool>? Bool { get; init; }
    public Func<FilterContext, string?, double>? Number { get; init; }
    /// <summary>Whether the context's fact matches the text value (the operator then only negates).</summary>
    public Func<FilterContext, string?, string, bool>? Text { get; init; }
    /// <summary>Accepts a text value at load; null accepts any. A typo in a value fails the load, not the spawn.</summary>
    public Func<string, bool>? ValidText { get; init; }
}

/// <summary>
/// The tests a filter may name. An instance (not a static list): a mod or a test adds its own test to its own registry, and the parser
/// rejects any name that is not in it. <see cref="CreateDefault"/> has the tests the shipped data uses.
/// </summary>
sealed class FilterRegistry
{
    private readonly Dictionary<string, FilterTestDefinition> _tests = new(StringComparer.Ordinal);

    private static readonly Lazy<FilterRegistry> Shared = new(CreateDefault);

    public static FilterRegistry Default => Shared.Value;

    public bool TryGet(string name, out FilterTestDefinition definition) => _tests.TryGetValue(name, out definition!);

    public IEnumerable<string> Names => _tests.Keys;

    public FilterRegistry Add(FilterTestDefinition definition)
    {
        if (!_tests.TryAdd(definition.Name, definition)) throw new InvalidOperationException($"Filter test '{definition.Name}' is registered twice.");
        return this;
    }

    public FilterRegistry AddBool(string name, Func<FilterContext, string?, bool> get, params string[] domains) =>
        Add(new FilterTestDefinition { Name = name, Kind = FilterValueKind.Bool, Bool = get, Domains = domains.ToHashSet() });

    public FilterRegistry AddNumber(string name, Func<FilterContext, string?, double> get, params string[] domains) =>
        Add(new FilterTestDefinition { Name = name, Kind = FilterValueKind.Number, Number = get, Domains = domains.ToHashSet() });

    public FilterRegistry AddText(string name, Func<FilterContext, string?, string, bool> matches, Func<string, bool>? valid = null, params string[] domains) =>
        Add(new FilterTestDefinition { Name = name, Kind = FilterValueKind.Text, Text = matches, ValidText = valid, Domains = domains.ToHashSet() });

    /// <summary>The tests of the shipped rules, named as the BDS packs name them.</summary>
    public static FilterRegistry CreateDefault()
    {
        var catalog = BiomeCatalog.Default;
        return new FilterRegistry()
            // light_level: the brightness of the cell. Without a domain it ignores the weather (animals); with the domain
            // "adjust_for_weather" rain and thunder dim it (the monsters' rule in the pack).
            .AddNumber("light_level", (c, domain) => c.Light(domain == "adjust_for_weather"), "adjust_for_weather")
            .AddText("is_difficulty", (c, _, v) => DifficultyRules.TryParse(v, out var d) && c.Difficulty == d,
                v => DifficultyRules.TryParse(v, out _))
            .AddText("has_biome_tag", (c, _, v) => c.Biome.Tags.Contains(v), catalog.HasTag)
            .AddText("is_biome", (c, _, v) => c.Biome.Id == v, catalog.HasBiome)
            // is_block: the block at the cell ("self", the default), under it ("down") or over it ("up").
            .AddText("is_block", (c, domain, v) => c.BlockNameAt(domain switch { "down" => -1, "up" => 1, _ => 0 }) == v, null, "self", "down", "up")
            .AddText("weather", (c, _, v) => v == WeatherName(c), v => v is "clear" or "rain" or "thunder")
            .AddBool("is_raining", (c, _) => c.World.Weather.Raining)
            .AddBool("is_thundering", (c, _) => c.World.Weather.Thunder)
            // is_daytime: the sun takes at most 3 levels off the sky (full day and the edges of it).
            .AddBool("is_daytime", (c, _) => LightModel.SkyDarkness(c.WorldTime) <= DaytimeMaxDarkness)
            // is_underground: something solid or opaque lies over the cell (it does not see the sky).
            .AddBool("is_underground", (c, _) => !c.SeesSky)
            .AddNumber("hourly_clock_time", (c, _) => (c.WorldTime % 24000 + 24000) % 24000);
    }

    /// <summary>The sun burns the undead while it takes at most this many levels off the sky.</summary>
    public const int DaytimeMaxDarkness = 3;

    private static string WeatherName(FilterContext c) => c.World.Weather.Thunder ? "thunder" : c.World.Weather.Raining ? "rain" : "clear";
}

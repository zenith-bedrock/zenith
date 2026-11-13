using System.Reflection;
using System.Text.Json;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>When in the day a spawn entry applies. Night is the stretch where the sky is dark enough for hostile mobs.</summary>
enum SpawnTime { Any, Day, Night }

/// <summary>A mob category: how many of its mobs the world keeps per online player, and how often a spawn round runs.</summary>
sealed record SpawnCategory(string Name, int CapPerPlayer, int IntervalTicks);

/// <summary>One candidate of natural spawning: what, where (biomes), when, how many at once, and its relative weight.</summary>
sealed record SpawnEntry(string Mob, SpawnCategory Category, int Weight, int GroupMin, int GroupMax, SpawnTime Time, IReadOnlySet<OverworldBiomeKind> Biomes)
{
    public bool AppliesTo(OverworldBiomeKind biome, int worldTime) =>
        Biomes.Contains(biome) && Time switch
        {
            SpawnTime.Night => IsNight(worldTime),
            SpawnTime.Day => !IsNight(worldTime),
            _ => true
        };

    /// <summary>The hostile window: dusk is over at 13000 and dawn starts at 23000 of the 24000-tick day.</summary>
    public static bool IsNight(int worldTime) => worldTime is >= 13000 and < 23000;
}

/// <summary>
/// The natural spawn table, loaded from the embedded <c>data/spawns.json</c> (hand-authored, not from a BDS capture). Every entry is
/// validated at load: an unknown category, biome or time, a bad group range or a non-positive weight fails loudly instead of
/// becoming a spawn that silently never happens.
/// </summary>
sealed class SpawnTable
{
    public const string ResourceName = "spawns.json";

    private static readonly Lazy<SpawnTable> Loaded = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return Parse(stream);
    });

    public static SpawnTable Default => Loaded.Value;

    public IReadOnlyList<SpawnCategory> Categories { get; }
    public IReadOnlyList<SpawnEntry> Entries { get; }

    private SpawnTable(IReadOnlyList<SpawnCategory> categories, IReadOnlyList<SpawnEntry> entries)
    {
        Categories = categories;
        Entries = entries;
    }

    public IEnumerable<SpawnEntry> In(SpawnCategory category) => Entries.Where(entry => entry.Category == category);

    public static SpawnTable Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var categories = new List<SpawnCategory>();
        foreach (var c in doc.RootElement.GetProperty("categories").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name)) throw Invalid("a category has no name");
            if (categories.Any(x => x.Name == name)) throw Invalid($"category '{name}' is defined twice");
            var cap = c.GetProperty("capPerPlayer").GetInt32();
            var interval = c.GetProperty("intervalTicks").GetInt32();
            if (cap <= 0) throw Invalid($"category '{name}' has capPerPlayer {cap}; it must be positive");
            if (interval <= 0) throw Invalid($"category '{name}' has intervalTicks {interval}; it must be positive");
            categories.Add(new SpawnCategory(name, cap, interval));
        }

        var entries = new List<SpawnEntry>();
        foreach (var e in doc.RootElement.GetProperty("spawns").EnumerateArray())
        {
            var mob = e.GetProperty("mob").GetString();
            if (string.IsNullOrWhiteSpace(mob)) throw Invalid("a spawn has no mob");
            var categoryName = e.GetProperty("category").GetString();
            var category = categories.FirstOrDefault(c => c.Name == categoryName)
                ?? throw Invalid($"'{mob}' names unknown category '{categoryName}'");
            var weight = e.GetProperty("weight").GetInt32();
            if (weight <= 0) throw Invalid($"'{mob}' has weight {weight}; it must be positive");
            var group = e.GetProperty("group");
            if (group.GetArrayLength() != 2) throw Invalid($"'{mob}' group must be [min, max]");
            var min = group[0].GetInt32();
            var max = group[1].GetInt32();
            if (min < 1 || max < min) throw Invalid($"'{mob}' has group [{min}, {max}]; need 1 <= min <= max");
            var timeText = e.GetProperty("time").GetString();
            if (!Enum.TryParse<SpawnTime>(timeText, ignoreCase: true, out var time)) throw Invalid($"'{mob}' has unknown time '{timeText}'");
            var biomes = new HashSet<OverworldBiomeKind>();
            foreach (var b in e.GetProperty("biomes").EnumerateArray())
            {
                var biomeText = b.GetString();
                if (!Enum.TryParse<OverworldBiomeKind>(biomeText, ignoreCase: true, out var biome)) throw Invalid($"'{mob}' names unknown biome '{biomeText}'");
                biomes.Add(biome);
            }

            if (biomes.Count == 0) throw Invalid($"'{mob}' lists no biomes");
            entries.Add(new SpawnEntry(mob, category, weight, min, max, time, biomes));
        }

        return new SpawnTable(categories, entries);
    }

    private static InvalidDataException Invalid(string message) => new($"{ResourceName}: {message}.");
}

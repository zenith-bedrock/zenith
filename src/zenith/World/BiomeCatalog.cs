using System.Reflection;
using System.Text.Json;

namespace Zenith.World;

/// <summary>What the rules need to know about a biome: its BDS id, its tags, and its climate (<c>minecraft:tags</c> and <c>minecraft:climate</c> of the pack).</summary>
sealed record BiomeInfo(OverworldBiomeKind Kind, string Id, IReadOnlySet<string> Tags, float Temperature, float Downfall, float SnowAccumulationMin, float SnowAccumulationMax);

/// <summary>The biomes Zenith generates, with the pack's tags and climate, from the embedded <c>data/biome_tags.json</c>. Every kind the generator can produce must be listed; a missing one fails at load.</summary>
sealed class BiomeCatalog
{
    public const string ResourceName = "biome_tags.json";

    private static readonly Lazy<BiomeCatalog> Loaded = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return Parse(stream);
    });

    public static BiomeCatalog Default => Loaded.Value;

    private readonly Dictionary<OverworldBiomeKind, BiomeInfo> _byKind;

    private BiomeCatalog(Dictionary<OverworldBiomeKind, BiomeInfo> byKind) => _byKind = byKind;

    public BiomeInfo Of(OverworldBiomeKind kind) => _byKind[kind];

    /// <summary>Whether some biome of the catalog carries this tag (a filter naming any other tag is a typo).</summary>
    public bool HasTag(string tag) => _byKind.Values.Any(b => b.Tags.Contains(tag));

    public bool HasBiome(string id) => _byKind.Values.Any(b => b.Id == id);

    public static BiomeCatalog Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var byKind = new Dictionary<OverworldBiomeKind, BiomeInfo>();
        foreach (var b in doc.RootElement.GetProperty("biomes").EnumerateArray())
        {
            var kindText = b.GetProperty("kind").GetString();
            if (!Enum.TryParse<OverworldBiomeKind>(kindText, ignoreCase: true, out var kind)) throw Invalid($"unknown biome kind '{kindText}'");
            if (byKind.ContainsKey(kind)) throw Invalid($"biome '{kindText}' is listed twice");
            var tags = b.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToHashSet(StringComparer.Ordinal);
            var snow = b.GetProperty("snowAccumulation");
            byKind[kind] = new BiomeInfo(kind, b.GetProperty("id").GetString()!, tags,
                b.GetProperty("temperature").GetSingle(), b.GetProperty("downfall").GetSingle(), snow[0].GetSingle(), snow[1].GetSingle());
        }

        foreach (var kind in Enum.GetValues<OverworldBiomeKind>())
            if (!byKind.ContainsKey(kind)) throw Invalid($"biome kind '{kind}' has no entry");
        return new BiomeCatalog(byKind);
    }

    private static InvalidDataException Invalid(string message) => new($"{ResourceName}: {message}.");
}

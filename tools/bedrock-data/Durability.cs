using System.Text.Json.Nodes;

namespace Zenith.BedrockData;

/// <summary>
/// Max-durability table for the embedded item palette. The BDS packs do not carry it (classic tools are native items),
/// so it comes from an open-source extraction (PrismarineJS minecraft-data, MIT: <c>data/bedrock/&lt;ver&gt;/items.json</c>),
/// matched to OUR palette BY NAME (numeric ids follow the registry order of each game version, so they are not an
/// identity): an item the source knows but the palette does not is reported as omitted instead of being carried as an
/// inert entry, and one whose numeric id differs (registered in another game version than our palette) is left out and reported as skewed.
/// </summary>
internal static class Durability
{
    internal sealed record Result(SortedDictionary<string, int> Items, IReadOnlyList<string> Omitted, IReadOnlyList<string> IdSkewed);

    public static Result Build(JsonArray source, JsonArray palette)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in palette)
            ids[(string)item!["name"]!] = (int)item["id"]!;

        var items = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var omitted = new List<string>();
        var skewed = new List<string>();
        foreach (var entry in source)
        {
            if (entry!["maxDurability"] is not { } durability) continue;
            var name = "minecraft:" + (string)entry["name"]!;
            var max = (int)durability;
            if (max <= 0) throw new InvalidDataException($"{name}: max durability {max} is not positive.");

            if (!ids.TryGetValue(name, out var paletteId)) { omitted.Add(name); continue; }
            // Registered after a different version of the game than our palette: the value cannot be vouched for.
            if ((int)entry["id"]! != paletteId) { skewed.Add(name); continue; }
            if (!items.TryAdd(name, max)) throw new InvalidDataException($"{name}: duplicated in the source.");
        }

        omitted.Sort(StringComparer.Ordinal);
        skewed.Sort(StringComparer.Ordinal);
        return new Result(items, omitted, skewed);
    }
}

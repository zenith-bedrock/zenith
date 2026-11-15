using System.Text.Json.Nodes;

namespace Zenith.BedrockData;

internal static class StackSizes
{
    /// <summary>Name-based facts; source numeric IDs never become runtime identities.</summary>
    public static SortedDictionary<string, int> Build(JsonArray source)
    {
        var all = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in source)
        {
            var name = "minecraft:" + (string)entry!["name"]!;
            var size = (int)entry["stackSize"]!;
            if (size is < 1 or > 64) throw new InvalidDataException($"{name}: invalid stack size {size}.");
            if (!all.TryAdd(name, size)) throw new InvalidDataException($"{name}: duplicate source entry.");
        }
        return new SortedDictionary<string, int>(all.Where(kv => kv.Value != 64)
            .ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal);
    }
}

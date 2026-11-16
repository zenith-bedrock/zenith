using System.Collections.Frozen;
using System.Text.Json;

namespace Zenith.World;

/// <summary>Physical properties of one block type (data/block_properties.json).</summary>
readonly record struct BlockProperty(
    double Hardness,
    double BlastResistance,
    int Brightness,
    int Flammability,
    int FlameEncouragement)
{
    /// <summary>Hardness -1 marks blocks that survival can never break (bedrock, barriers, portals frames…).</summary>
    public bool IsUnbreakable => Hardness < 0;
}

/// <summary>
/// Loader for the embedded <c>block_properties.json</c>: name → <see cref="BlockProperty"/>. Missing names mean
/// "no recorded properties" and the catalog treats them as not survival-breakable.
/// </summary>
static class BlockPropertiesLoader
{
    public const string ResourceName = "block_properties.json";

    public static FrozenDictionary<string, BlockProperty> FromEmbeddedResource(System.Reflection.Assembly? assembly = null)
    {
        assembly ??= typeof(BlockPropertiesLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return FromJsonStream(stream);
    }

    public static FrozenDictionary<string, BlockProperty> FromJsonStream(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (!root.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("block_properties.json missing object 'blocks'.");

        var map = new Dictionary<string, BlockProperty>(StringComparer.Ordinal);
        foreach (var block in blocks.EnumerateObject())
        {
            var row = block.Value;
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 5)
                throw new InvalidOperationException($"block_properties.json row for '{block.Name}' must have 5 columns.");
            map[block.Name] = new BlockProperty(
                row[0].GetDouble(), row[1].GetDouble(), (int)row[2].GetDouble(),
                row[3].GetInt32(), row[4].GetInt32());
        }
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

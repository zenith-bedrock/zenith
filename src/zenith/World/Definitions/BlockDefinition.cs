using System.Collections.Frozen;
using System.Text.Json;

namespace Zenith.World;

/// <summary>What breaking a block yields when that is not "the block itself": a block, an item, or nothing (<see cref="Count"/> 0).</summary>
readonly record struct DropSpec(string? Block, string? Item, int Count)
{
    public bool DropsNothing => Count == 0;
}

/// <summary>
/// The server rules for one block type, as data (<c>data/block_definitions.json</c>): how it is classified, which tool
/// harvests it, what it is made of and what it drops. Physical numbers (hardness…) live in <see cref="BlockProperty"/>.
/// A name missing from the file is <see cref="BlockKind.Unsupported"/>: unsimulated blocks are neither placeable nor
/// breakable.
/// </summary>
sealed record BlockDefinition(
    BlockKind Kind,
    ToolKind Tool = ToolKind.None,
    ToolTier Tier = ToolTier.None,
    bool RequiresTool = false,
    BlockTraits Traits = BlockTraits.None,
    DropSpec? Drop = null,
    string? MergesInto = null);

/// <summary>Reads <c>block_definitions.json</c>: name → <see cref="BlockDefinition"/>.</summary>
static class BlockDefinitionLoader
{
    public const string ResourceName = "block_definitions.json";

    public static FrozenDictionary<string, BlockDefinition> FromEmbeddedResource(System.Reflection.Assembly? assembly = null)
    {
        assembly ??= typeof(BlockDefinitionLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return FromJsonStream(stream);
    }

    public static FrozenDictionary<string, BlockDefinition> FromJsonStream(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{ResourceName} missing object 'blocks'.");

        var map = new Dictionary<string, BlockDefinition>(StringComparer.Ordinal);
        foreach (var block in blocks.EnumerateObject())
            map[block.Name] = Parse(block.Name, block.Value);
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static BlockDefinition Parse(string name, JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{ResourceName}: '{name}' must be an object.");

        var kind = Required<BlockKind>(name, row, "kind");
        var traits = BlockTraits.None;
        if (row.TryGetProperty("traits", out var list))
            foreach (var trait in list.EnumerateArray())
                traits |= ParseEnum<BlockTraits>(name, "traits", trait.GetString());

        DropSpec? drop = null;
        if (row.TryGetProperty("drop", out var d))
        {
            var count = d.TryGetProperty("count", out var c) ? c.GetInt32() : 1;
            var block = d.TryGetProperty("block", out var b) ? b.GetString() : null;
            var item = d.TryGetProperty("item", out var i) ? i.GetString() : null;
            if (block is not null && item is not null)
                throw new InvalidOperationException($"{ResourceName}: '{name}' drop names both a block and an item.");
            drop = new DropSpec(block, item, count);
        }

        return new BlockDefinition(
            kind,
            Optional<ToolKind>(name, row, "tool"),
            Optional<ToolTier>(name, row, "tier"),
            row.TryGetProperty("requiresTool", out var requires) && requires.GetBoolean(),
            traits,
            drop,
            row.TryGetProperty("mergesInto", out var merges) ? merges.GetString() : null);
    }

    private static T Required<T>(string name, JsonElement row, string key) where T : struct, Enum =>
        row.TryGetProperty(key, out var value)
            ? ParseEnum<T>(name, key, value.GetString())
            : throw new InvalidOperationException($"{ResourceName}: '{name}' is missing '{key}'.");

    private static T Optional<T>(string name, JsonElement row, string key) where T : struct, Enum =>
        row.TryGetProperty(key, out var value) ? ParseEnum<T>(name, key, value.GetString()) : default;

    private static T ParseEnum<T>(string name, string key, string? text) where T : struct, Enum =>
        Enum.TryParse<T>(text, out var value) && Enum.IsDefined(value)
            ? value
            : throw new InvalidOperationException($"{ResourceName}: '{name}' has unknown {key} '{text}'.");
}

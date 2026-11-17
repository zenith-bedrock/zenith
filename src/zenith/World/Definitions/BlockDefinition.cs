using System.Collections.Frozen;
using System.Text.Json;

namespace Zenith.World;

/// <summary>What breaking a block yields when that is not "the block itself": a block, an item, or nothing (<see cref="Count"/> 0).</summary>
/// <param name="Count">The count, or the lowest count when <paramref name="CountMax"/> is above it (redstone drops 4 to 5).</param>
/// <param name="CountMax">The highest count; 0 or at most <paramref name="Count"/> means exactly <paramref name="Count"/>.</param>
/// <param name="Chance">For an entry of <paramref name="Instead"/> or <paramref name="Bonus"/>: how likely it is to drop (0 to 1).</param>
/// <param name="Instead">Entries tried in order; the first whose chance comes up drops in place of the main drop (gravel gives flint one time in ten).</param>
/// <param name="Bonus">Entries that each roll on their own on top of the main drop (leaves may give a sapling, a stick, an apple).</param>
readonly record struct DropSpec(
    string? Block, string? Item, int Count, int CountMax = 0, float Chance = 1f, DropSpec[]? Instead = null, DropSpec[]? Bonus = null)
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

        DropSpec? drop = row.TryGetProperty("drop", out var d) ? ParseDrop(name, d) : null;

        return new BlockDefinition(
            kind,
            Optional<ToolKind>(name, row, "tool"),
            Optional<ToolTier>(name, row, "tier"),
            row.TryGetProperty("requiresTool", out var requires) && requires.GetBoolean(),
            traits,
            drop,
            row.TryGetProperty("mergesInto", out var merges) ? merges.GetString() : null);
    }

    private static DropSpec ParseDrop(string name, JsonElement d)
    {
        var count = d.TryGetProperty("count", out var c) ? c.GetInt32() : 1;
        var countMax = d.TryGetProperty("countMax", out var cm) ? cm.GetInt32() : 0;
        var chance = d.TryGetProperty("chance", out var ch) ? ch.GetSingle() : 1f;
        var block = d.TryGetProperty("block", out var b) ? b.GetString() : null;
        var item = d.TryGetProperty("item", out var i) ? i.GetString() : null;
        if (block is not null && item is not null)
            throw new InvalidOperationException($"{ResourceName}: '{name}' drop names both a block and an item.");
        if (count < 0 || countMax < 0 || chance is < 0f or > 1f)
            throw new InvalidOperationException($"{ResourceName}: '{name}' drop has a negative count or a chance outside 0 to 1.");
        return new DropSpec(block, item, count, countMax, chance, ParseDropList(name, d, "instead"), ParseDropList(name, d, "bonus"));
    }

    private static DropSpec[]? ParseDropList(string name, JsonElement d, string key) =>
        d.TryGetProperty(key, out var list) ? [.. list.EnumerateArray().Select(entry => ParseDrop(name, entry))] : null;

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

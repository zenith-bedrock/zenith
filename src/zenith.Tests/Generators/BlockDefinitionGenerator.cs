using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Zenith.World;

namespace Zenith.Tests.Generators;

/// <summary>
/// Produces <c>block_definitions.json</c> from the name heuristics in <see cref="BlockSupportRules"/> and friends.
/// The heuristics live only here: the server reads the file, so a rule is changed by editing a row, not by recompiling.
/// Running with <see cref="OutputVariable"/> set merges instead of overwriting — rows already in the file win, only
/// blocks the file does not list yet are generated. Run it after a palette update, then review the diff.
/// </summary>
static class BlockDefinitionGenerator
{
    public const string OutputVariable = "ZENITH_REGEN_BLOCK_DEFINITIONS";

    private const string Note =
        "Server rules per block type: kind, harvest tool/tier, traits, drop and slab merge target. Blocks not listed are unsupported " +
        "(neither placeable nor breakable). Edit rows freely; tests/Generators/BlockDefinitionGenerator adds rows for new palette blocks " +
        "without touching existing ones.";

    private static readonly Dictionary<string, string> OreDrops = new(StringComparer.Ordinal)
    {
        ["minecraft:coal_ore"] = "minecraft:coal",
        ["minecraft:deepslate_coal_ore"] = "minecraft:coal",
        ["minecraft:diamond_ore"] = "minecraft:diamond",
        ["minecraft:deepslate_diamond_ore"] = "minecraft:diamond",
        ["minecraft:redstone_ore"] = "minecraft:redstone",
        ["minecraft:deepslate_redstone_ore"] = "minecraft:redstone",
        ["minecraft:lapis_ore"] = "minecraft:lapis_lazuli",
        ["minecraft:deepslate_lapis_ore"] = "minecraft:lapis_lazuli"
    };

    /// <summary>Rows for every supported block of the palette, from the heuristics.</summary>
    public static SortedDictionary<string, BlockDefinition> Generate(BlockPalette palette)
    {
        var states = BlockStateTable.Build(palette);
        var rows = new SortedDictionary<string, BlockDefinition>(StringComparer.Ordinal);
        foreach (var family in states.Families)
        {
            family.Kind = Classify(family);
            if (family.Kind == BlockKind.Unsupported) continue;
            family.Traits = BlockSupportRules.TraitsOf(family);

            var (tool, requires, tier) = BlockToolRules.Classify(family.Name);
            rows[family.Name] = new BlockDefinition(
                family.Kind, tool, tier, requires, family.Traits, DropOf(family),
                family.Kind == BlockKind.Slab ? DoubleSlabOf(family.Name) : null);
        }
        return rows;
    }

    /// <summary>Existing rows win; generated rows fill in the blocks the file does not list.</summary>
    public static void Merge(string path, SortedDictionary<string, BlockDefinition> generated)
    {
        var merged = new SortedDictionary<string, BlockDefinition>(generated, StringComparer.Ordinal);
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            foreach (var (name, row) in BlockDefinitionLoader.FromJsonStream(stream))
                merged[name] = row;
        }
        File.WriteAllText(path, Serialize(merged), new UTF8Encoding(false));
    }

    public static string Serialize(SortedDictionary<string, BlockDefinition> rows)
    {
        var sb = new StringBuilder();
        sb.Append("{\"note\":").Append(JsonSerializer.Serialize(Note)).Append(",\"blocks\":{\n");
        var first = true;
        foreach (var (name, row) in rows)
        {
            if (!first) sb.Append(",\n");
            first = false;
            sb.Append("  ").Append(JsonSerializer.Serialize(name)).Append(':').Append(Row(row));
        }
        return sb.Append("\n}}\n").ToString();
    }

    private static string Row(BlockDefinition row)
    {
        var parts = new List<string> { $"\"kind\":\"{row.Kind}\"" };
        if (row.Tool != ToolKind.None) parts.Add($"\"tool\":\"{row.Tool}\"");
        if (row.Tier != ToolTier.None) parts.Add($"\"tier\":\"{row.Tier}\"");
        if (row.RequiresTool) parts.Add("\"requiresTool\":true");
        if (row.Traits != BlockTraits.None)
        {
            var names = Enum.GetValues<BlockTraits>().Where(t => t != BlockTraits.None && row.Traits.HasFlag(t));
            parts.Add("\"traits\":[" + string.Join(',', names.Select(t => $"\"{t}\"")) + "]");
        }
        if (row.Drop is { } drop)
        {
            var d = new List<string>();
            if (drop.Block is not null) d.Add($"\"block\":{JsonSerializer.Serialize(drop.Block)}");
            if (drop.Item is not null) d.Add($"\"item\":{JsonSerializer.Serialize(drop.Item)}");
            if (drop.Count != 1) d.Add($"\"count\":{drop.Count}");
            parts.Add("\"drop\":{" + string.Join(',', d) + "}");
        }
        if (row.MergesInto is not null) parts.Add($"\"mergesInto\":{JsonSerializer.Serialize(row.MergesInto)}");
        return "{" + string.Join(',', parts) + "}";
    }

    private static string DoubleSlabOf(string slab) => slab[..^"_slab".Length] + "_double_slab";

    private static DropSpec? DropOf(BlockFamily family)
    {
        var name = family.Name;
        if (OreDrops.TryGetValue(name, out var gem)) return new DropSpec(null, gem, 1);
        // A door is one item, whichever half is broken; the item carries the block name.
        if (family.Kind == BlockKind.Door) return new DropSpec(null, name, 1);

        if (!BlockDropRules.TryRedirect(name, out var redirect, out var count)) return null;
        return count == 0 ? new DropSpec(null, null, 0) : new DropSpec(redirect, null, count);
    }

    // The state-signature classification the catalog used before the rules became data.
    private static BlockKind Classify(BlockFamily f)
    {
        var name = f.Name;
        if (BlockSupportRules.IsExcluded(name)) return BlockKind.Unsupported;
        var count = f.Properties.Length;

        if (count == 0)
            return BlockSupportRules.IsPlant(name) ? BlockKind.Plant : BlockKind.Cube;

        if (count == 3 && f.Has(StateKeys.StairFacing) && f.Has(StateKeys.UpsideDown) && f.Has(StateKeys.StairCorner))
            return BlockKind.Stairs;

        if (f.Has(StateKeys.PillarAxis) && f.Properties.All(p => p.Key == StateKeys.PillarAxis || p.Key == StateKeys.Deprecated))
            return BlockKind.Pillar;

        if (f.Has(StateKeys.WallPost)) return BlockKind.Wall;
        if (f.Has(StateKeys.UpperBlock) && f.Has(StateKeys.DoorHinge) && BlockNames.IsDoor(name)) return BlockKind.Door;
        if (f.Has(StateKeys.Open) && f.Has(StateKeys.UpsideDown) && f.Has(StateKeys.TrapdoorFacing) && BlockNames.IsTrapdoor(name))
            return BlockKind.Trapdoor;
        if (f.Has(StateKeys.Open) && f.Has(StateKeys.InWall) && BlockNames.IsFenceGate(name)) return BlockKind.FenceGate;

        if (count == 1)
        {
            if (f.Has(StateKeys.VerticalHalf))
            {
                if (BlockNames.IsDoubleSlab(name)) return BlockKind.DoubleSlab;
                return BlockNames.IsSlab(name) ? BlockKind.Slab : BlockKind.Unsupported;
            }
            if (f.Has(StateKeys.PillarAxis)) return BlockKind.Pillar;
            if (f.Has(StateKeys.Age) && BlockSupportRules.IsPlant(name)) return BlockKind.Plant;
            if (f.Has(StateKeys.TorchFacing) && BlockSupportRules.IsTorch(name)) return BlockKind.Torch;
        }

        return BlockSupportRules.IsCosmeticState(f) ? BlockKind.Cube : BlockKind.Unsupported;
    }
}

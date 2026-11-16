using System.Collections.Frozen;

namespace Zenith.World;

/// <summary>
/// How a block is placed (which block-state properties the server must decide) and whether Zenith supports it at all.
/// <see cref="Unsupported"/> blocks have behaviour the server cannot yet honour (doors, containers, redstone, liquids…),
/// so they are neither placeable nor breakable by the derived rules; curated entries can still opt in explicitly.
/// </summary>
enum BlockKind : byte
{
    Unsupported = 0,

    /// <summary>No server-decided state — placed exactly as held (cubes, glass, panes, fences, leaves, ores…).</summary>
    Cube,

    /// <summary><c>pillar_axis</c> from the clicked face (logs, pillars, chains).</summary>
    Pillar,

    /// <summary><c>weirdo_direction</c> from yaw and <c>upside_down_bit</c> from face/cursor height.</summary>
    Stairs,

    /// <summary><c>minecraft:vertical_half</c> from face/cursor height; merges into a double slab on the matching half.</summary>
    Slab,

    /// <summary>Two slabs in one cell — only ever produced by merging, never placed from an item.</summary>
    DoubleSlab,

    /// <summary>Ground decoration: not solid, needs support underneath and breaks instantly.</summary>
    Plant,

    /// <summary><c>torch_facing_direction</c> from the clicked face.</summary>
    Torch,

    /// <summary>Two-block door: lower half placed, upper half added with it; right click toggles <c>open_bit</c> on both.</summary>
    Door,

    /// <summary>Facing from yaw, half from face/cursor height; right click toggles <c>open_bit</c>.</summary>
    Trapdoor,

    /// <summary>Facing from yaw; right click toggles <c>open_bit</c>.</summary>
    FenceGate,

    /// <summary>Connection types and post recomputed from neighbours on every placement or removal next to it.</summary>
    Wall
}

/// <summary>
/// Name- and state-driven block knowledge built once from the palette's full state model plus the imported physical
/// properties. It replaces per-name allow-lists: a block is placeable/breakable because of what it <i>is</i>
/// (its state signature and material), with hand-curated profiles still taking precedence where they exist.
/// </summary>
sealed class BlockCatalog
{
    private readonly BlockPalette _palette;
    private readonly FrozenDictionary<string, BlockProperty> _properties;
    private readonly Dictionary<string, BlockKind> _kindByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DigProfile?> _digByName = new(StringComparer.Ordinal);

    public BlockCatalog(BlockPalette palette, FrozenDictionary<string, BlockProperty> properties)
    {
        _palette = palette;
        _properties = properties;
    }

    public BlockPalette Palette => _palette;

    public bool TryGetProperty(string name, out BlockProperty property) => _properties.TryGetValue(name, out property);

    public BlockKind KindOf(string name)
    {
        lock (_kindByName)
        {
            if (!_kindByName.TryGetValue(name, out var kind))
                _kindByName[name] = kind = Classify(name);
            return kind;
        }
    }

    public BlockKind KindOf(int runtimeId) =>
        _palette.TryGetName(runtimeId, out var name) ? KindOf(name) : BlockKind.Unsupported;

    public bool IsPlaceable(int runtimeId) =>
        KindOf(runtimeId) is not (BlockKind.Unsupported or BlockKind.DoubleSlab);

    /// <summary>Blocks that must not obstruct movement or placement checks (flowers, grass, torches, open doors/gates/trapdoors).</summary>
    public bool IsPassable(int runtimeId)
    {
        var kind = KindOf(runtimeId);
        if (kind is BlockKind.Plant or BlockKind.Torch) return true;
        return kind is BlockKind.Door or BlockKind.Trapdoor or BlockKind.FenceGate &&
               _palette.TryGetState(runtimeId, out var state) && state.Get("open_bit") == "1";
    }

    /// <summary>Derived dig profile for a supported, breakable block; false for unsupported or unbreakable ones.</summary>
    public bool TryGetDigProfile(int runtimeId, out DigProfile profile)
    {
        profile = default;
        if (!_palette.TryGetName(runtimeId, out var name)) return false;
        DigProfile? derived;
        lock (_digByName)
        {
            if (!_digByName.TryGetValue(name, out derived))
                _digByName[name] = derived = DeriveDig(name);
        }
        if (derived is not { } value) return false;
        profile = value;
        return true;
    }

    private DigProfile? DeriveDig(string name)
    {
        var kind = KindOf(name);
        if (kind == BlockKind.Unsupported) return null;
        if (!_properties.TryGetValue(name, out var property) || property.IsUnbreakable) return null;

        var (tool, requires, tier) = BlockToolRules.Classify(name);
        // BreakDuration divides by DestroySpeed, so instant blocks use the smallest positive speed (indistinguishable
        // from instant at 20 TPS) — the same convention as the curated ground-cover rows (ADR §137).
        var speed = property.Hardness <= 0 ? 0.1 : property.Hardness;
        return new DigProfile(speed, requires ? tool : ToolKind.None, tool, requires, tier);
    }

    private BlockKind Classify(string name)
    {
        if (BlockSupportRules.IsExcluded(name)) return BlockKind.Unsupported;
        var properties = _palette.PropertiesOf(name);

        if (properties is null)
            return BlockSupportRules.IsPlant(name) ? BlockKind.Plant : BlockKind.Cube;

        if (properties.ContainsKey("weirdo_direction") && properties.ContainsKey("upside_down_bit") &&
            properties.ContainsKey("minecraft:corner") && properties.Count == 3)
            return BlockKind.Stairs;

        if (properties.ContainsKey("pillar_axis") && properties.Keys.All(k => k is "pillar_axis" or "deprecated"))
            return BlockKind.Pillar;

        if (properties.ContainsKey("wall_post_bit")) return BlockKind.Wall;
        if (properties.ContainsKey("upper_block_bit") && properties.ContainsKey("door_hinge_bit") &&
            name.EndsWith("_door", StringComparison.Ordinal)) return BlockKind.Door;
        if (properties.ContainsKey("open_bit") && properties.ContainsKey("upside_down_bit") &&
            properties.ContainsKey("direction") && name.EndsWith("trapdoor", StringComparison.Ordinal)) return BlockKind.Trapdoor;
        if (properties.ContainsKey("open_bit") && properties.ContainsKey("in_wall_bit") &&
            name.EndsWith("fence_gate", StringComparison.Ordinal)) return BlockKind.FenceGate;

        if (properties.Count == 1)
        {
            if (properties.ContainsKey("minecraft:vertical_half"))
            {
                if (name.EndsWith("_double_slab", StringComparison.Ordinal)) return BlockKind.DoubleSlab;
                return name.EndsWith("_slab", StringComparison.Ordinal) ? BlockKind.Slab : BlockKind.Unsupported;
            }
            if (properties.ContainsKey("pillar_axis")) return BlockKind.Pillar;
            if (properties.ContainsKey("age_bit") && BlockSupportRules.IsPlant(name)) return BlockKind.Plant;
            if (properties.ContainsKey("torch_facing_direction") && BlockSupportRules.IsTorch(name)) return BlockKind.Torch;
        }

        // Panes/leaves carry cosmetic state only: the default permutation is exactly what vanilla places.
        if (BlockSupportRules.IsCosmeticState(properties)) return BlockKind.Cube;
        return BlockKind.Unsupported;
    }
}

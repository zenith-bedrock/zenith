using System.Collections.Frozen;

namespace Zenith.World;

/// <summary>
/// How a block is placed (which block-state properties the server must decide) and whether Zenith supports it at all.
/// <see cref="Unsupported"/> blocks have behaviour the server cannot yet honour (containers, redstone, liquids…),
/// so they are neither placeable nor breakable by the derived rules; curated entries can still opt in explicitly.
/// Each kind has one <see cref="BlockShape"/> that owns its rules.
/// </summary>
enum BlockKind : byte
{
    Unsupported = 0,

    /// <summary>No server-decided state — placed exactly as held (cubes, glass, panes, fences, leaves, ores…).</summary>
    Cube,

    /// <summary>Axis from the clicked face (logs, pillars, chains).</summary>
    Pillar,

    /// <summary>Facing from yaw, upside-down from face / cursor height.</summary>
    Stairs,

    /// <summary>Half from face / cursor height; merges into a double slab on the matching half.</summary>
    Slab,

    /// <summary>Two slabs in one cell — only ever produced by merging, never placed from an item.</summary>
    DoubleSlab,

    /// <summary>Ground decoration: not solid, needs support underneath and breaks instantly.</summary>
    Plant,

    /// <summary>Facing from the clicked face; needs the block it hangs on.</summary>
    Torch,

    /// <summary>Two-block door: lower half placed, upper half added with it; right click toggles both.</summary>
    Door,

    /// <summary>Facing from yaw, half from face/cursor height; right click toggles.</summary>
    Trapdoor,

    /// <summary>Facing from yaw; right click toggles.</summary>
    FenceGate,

    /// <summary>Connection types and post recomputed from neighbours on every placement or removal next to it.</summary>
    Wall,

    /// <summary>Leaves: placed by a player they are persistent (never decay); grown by world-gen they are not.</summary>
    Leaves
}

/// <summary>
/// Name- and state-driven block knowledge built once from the palette's full state model plus the imported physical
/// properties. A block is placeable/breakable because of what it <i>is</i> (its state signature and material), with
/// hand-curated profiles still taking precedence where they exist. Everything is decided when the catalog is built, so
/// the runtime queries are lookups: no locks, no string work.
/// </summary>
sealed class BlockCatalog
{
    private readonly FrozenDictionary<string, BlockProperty> _properties;

    public BlockCatalog(
        BlockPalette palette,
        FrozenDictionary<string, BlockProperty> properties,
        FrozenDictionary<string, BlockDefinition> definitions)
    {
        Palette = palette;
        _properties = properties;
        States = BlockStateTable.Build(palette);

        var problems = new List<string>();
        foreach (var family in States.Families)
        {
            if (properties.TryGetValue(family.Name, out var property))
            {
                family.Flammability = property.Flammability;
                family.FlameEncouragement = property.FlameEncouragement;
            }

            // A block the definitions do not list stays Unsupported: neither placeable nor breakable.
            if (!definitions.TryGetValue(family.Name, out var definition)) continue;
            family.Definition = definition;
            family.Kind = definition.Kind;
            family.Traits = definition.Traits;
            Validate(family, definition, problems);
            family.Dig = DeriveDig(family, definition);
        }

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"block_definitions.json does not match the palette ({problems.Count} problems): " +
                string.Join("; ", problems.Take(12)));
    }

    /// <summary>Definitions are data, so a kind that the block cannot honour is caught here, loudly, rather than at placement.</summary>
    private void Validate(BlockFamily family, BlockDefinition definition, List<string> problems)
    {
        foreach (var key in BlockShapes.ForKind(definition.Kind).RequiredKeys)
            if (!family.Has(key))
                problems.Add($"{family.Name} is {definition.Kind} but has no '{key.Name}'");

        if (definition.MergesInto is { } merged)
        {
            if (States.TryGetFamily(merged, out var target)) family.DoubleSlab = target;
            else problems.Add($"{family.Name} merges into unknown block '{merged}'");
        }
        else if (definition.Kind == BlockKind.Slab)
        {
            problems.Add($"{family.Name} is a slab without 'mergesInto'");
        }
    }

    public BlockPalette Palette { get; }

    /// <summary>Typed state access by runtime id.</summary>
    public BlockStateTable States { get; }

    public bool TryGetProperty(string name, out BlockProperty property) => _properties.TryGetValue(name, out property);

    public BlockKind KindOf(string name) =>
        States.TryGetFamily(name, out var family) ? family.Kind : BlockKind.Unsupported;

    public BlockKind KindOf(int runtimeId) => States.FamilyOf(runtimeId)?.Kind ?? BlockKind.Unsupported;

    public bool IsPlaceable(int runtimeId) =>
        KindOf(runtimeId) is not (BlockKind.Unsupported or BlockKind.DoubleSlab);

    /// <summary>Blocks that must not obstruct movement or placement checks (flowers, grass, torches, open doors/gates/trapdoors).</summary>
    public bool IsPassable(int runtimeId)
    {
        var kind = KindOf(runtimeId);
        if (kind is BlockKind.Plant or BlockKind.Torch) return true;
        return kind is BlockKind.Door or BlockKind.Trapdoor or BlockKind.FenceGate && States.Bit(runtimeId, StateKeys.Open);
    }

    /// <summary>Derived dig profile for a supported, breakable block; false for unsupported or unbreakable ones.</summary>
    public bool TryGetDigProfile(int runtimeId, out DigProfile profile)
    {
        profile = default;
        if (States.FamilyOf(runtimeId)?.Dig is not { } derived) return false;
        profile = derived;
        return true;
    }

    private DigProfile? DeriveDig(BlockFamily family, BlockDefinition definition)
    {
        if (definition.Kind == BlockKind.Unsupported) return null;
        if (!_properties.TryGetValue(family.Name, out var property) || property.IsUnbreakable) return null;

        // BreakDuration divides by DestroySpeed, so instant blocks use the smallest positive speed (indistinguishable
        // from instant at 20 TPS) — the same convention as the curated ground-cover rows (ADR §137).
        var speed = property.Hardness <= 0 ? 0.1 : property.Hardness;
        var requires = definition.RequiresTool;
        return new DigProfile(speed, requires ? definition.Tool : ToolKind.None, definition.Tool, requires, definition.Tier);
    }
}

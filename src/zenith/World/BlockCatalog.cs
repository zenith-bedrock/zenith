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
    Wall
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

    public BlockCatalog(BlockPalette palette, FrozenDictionary<string, BlockProperty> properties)
    {
        Palette = palette;
        _properties = properties;
        States = BlockStateTable.Build(palette);

        foreach (var family in States.Families)
        {
            family.Kind = Classify(family);
            family.Traits = BlockSupportRules.TraitsOf(family);
            family.Dig = DeriveDig(family);
        }

        // Slabs know the double slab they merge into, so merging never builds a name at runtime.
        foreach (var family in States.Families)
        {
            if (family.Kind != BlockKind.Slab) continue;
            var doubled = string.Concat(family.Name.AsSpan(0, family.Name.Length - "_slab".Length), "_double_slab");
            if (States.TryGetFamily(doubled, out var doubleFamily)) family.DoubleSlab = doubleFamily;
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

    private DigProfile? DeriveDig(BlockFamily family)
    {
        if (family.Kind == BlockKind.Unsupported) return null;
        if (!_properties.TryGetValue(family.Name, out var property) || property.IsUnbreakable) return null;

        var (tool, requires, tier) = BlockToolRules.Classify(family.Name);
        // BreakDuration divides by DestroySpeed, so instant blocks use the smallest positive speed (indistinguishable
        // from instant at 20 TPS) — the same convention as the curated ground-cover rows (ADR §137).
        var speed = property.Hardness <= 0 ? 0.1 : property.Hardness;
        return new DigProfile(speed, requires ? tool : ToolKind.None, tool, requires, tier);
    }

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

        // Panes/leaves carry cosmetic state only: the default permutation is exactly what vanilla places.
        return BlockSupportRules.IsCosmeticState(f) ? BlockKind.Cube : BlockKind.Unsupported;
    }
}

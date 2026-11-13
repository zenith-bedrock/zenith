using System.Collections.Frozen;
using System.Collections.Generic;

namespace Zenith.World;

enum ToolKind : byte
{
    None = 0,
    Pickaxe = 1,
    Axe = 2,
    Shovel = 3
}

enum ToolTier : byte
{
    None = 0,
    Wood = 1,
    Stone = 2,
    Iron = 3,
    Diamond = 4
}

readonly record struct ToolInfo(ToolKind Kind, ToolTier Tier)
{
    public static ToolInfo None => new(ToolKind.None, ToolTier.None);

    /// <summary>Dragonfly / wiki BaseMiningEfficiency (hand = 1).</summary>
    public double BaseMiningEfficiency => Tier switch
    {
        ToolTier.Wood => 2,
        ToolTier.Stone => 4,
        ToolTier.Iron => 6,
        ToolTier.Diamond => 8,
        _ => 1
    };
}

/// <summary>
/// Curated dig tool profiles (ADR §27 / §55). Public façade name is <c>Tools</c>;
/// this <b>is</b> the sparse ToolProfiles map (kind + tier by item network id).
/// Inventory stores tools as <see cref="StackId"/> with <see cref="StackKind.Item"/>.
/// </summary>
static class Tools
{
    private static readonly object LoadGate = new();

    /// <summary>
    /// One immutable generation of the tool tables. A (re)load builds a whole new <see cref="Table"/> off to the side and publishes it
    /// with a single reference write, so a reader on another thread sees either the old tables or the new ones, never a half-filled map.
    /// </summary>
    private sealed record Table(
        FrozenDictionary<int, ToolInfo> ByNetworkId,
        FrozenDictionary<int, string> NameByNetworkId,
        FrozenDictionary<string, int> NetworkIdByName);

    private static volatile Table? _table;

    public static IReadOnlyList<string> CuratedNames { get; } =
    [
        "minecraft:wooden_pickaxe",
        "minecraft:wooden_axe",
        "minecraft:wooden_shovel",
        "minecraft:stone_pickaxe",
        "minecraft:stone_axe",
        "minecraft:stone_shovel",
        "minecraft:iron_pickaxe",
        "minecraft:iron_axe",
        "minecraft:iron_shovel",
        "minecraft:diamond_pickaxe",
        "minecraft:diamond_axe",
        "minecraft:diamond_shovel"
    ];

    public static void EnsureLoaded() => _ = Current;

    private static Table Current
    {
        get
        {
            if (_table is { } table) return table;
            lock (LoadGate)
                return _table ??= Build(ItemPaletteLoader.FromEmbeddedResource());
        }
    }

    public static void Load(ItemPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var table = Build(palette);
        lock (LoadGate)
            _table = table;
    }

    private static Table Build(ItemPalette palette)
    {
        var byNetworkId = new Dictionary<int, ToolInfo>();
        var nameByNetworkId = new Dictionary<int, string>();
        var networkIdByName = new Dictionary<string, int>(StringComparer.Ordinal);

        Register(palette, "minecraft:wooden_pickaxe", ToolKind.Pickaxe, ToolTier.Wood, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:wooden_axe", ToolKind.Axe, ToolTier.Wood, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:wooden_shovel", ToolKind.Shovel, ToolTier.Wood, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:stone_pickaxe", ToolKind.Pickaxe, ToolTier.Stone, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:stone_axe", ToolKind.Axe, ToolTier.Stone, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:stone_shovel", ToolKind.Shovel, ToolTier.Stone, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:iron_pickaxe", ToolKind.Pickaxe, ToolTier.Iron, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:iron_axe", ToolKind.Axe, ToolTier.Iron, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:iron_shovel", ToolKind.Shovel, ToolTier.Iron, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:diamond_pickaxe", ToolKind.Pickaxe, ToolTier.Diamond, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:diamond_axe", ToolKind.Axe, ToolTier.Diamond, byNetworkId, nameByNetworkId, networkIdByName);
        Register(palette, "minecraft:diamond_shovel", ToolKind.Shovel, ToolTier.Diamond, byNetworkId, nameByNetworkId, networkIdByName);
        return new Table(byNetworkId.ToFrozenDictionary(), nameByNetworkId.ToFrozenDictionary(), networkIdByName.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// Duplicates are checked within one pass only: <see cref="Load"/> re-running with a different <see cref="ItemPalette"/> is
    /// deliberate (matches <see cref="CreativeCatalog.CreateDefault(ItemPalette)"/>'s real call site), but two curated names resolving
    /// to the same network id within one pass is always a contributor mistake, never a valid state.
    /// </summary>
    private static void Register(
        ItemPalette palette, string name, ToolKind kind, ToolTier tier,
        Dictionary<int, ToolInfo> byNetworkId, Dictionary<int, string> nameByNetworkId, Dictionary<string, int> networkIdByName)
    {
        var id = palette.Require(name);
        if (!byNetworkId.TryAdd(id, new ToolInfo(kind, tier)))
            throw new InvalidOperationException($"Duplicate tool network id {id} ('{name}' collides with '{nameByNetworkId[id]}').");
        nameByNetworkId[id] = name;
        networkIdByName[name] = id;
    }

    public static bool TryAsTool(int stackTypeId, out ToolInfo tool)
    {
        if (Current.ByNetworkId.TryGetValue(stackTypeId, out tool))
            return true;
        tool = ToolInfo.None;
        return false;
    }

    public static ToolInfo AsTool(int stackTypeId) =>
        TryAsTool(stackTypeId, out var tool) ? tool : ToolInfo.None;

    public static bool TryGetName(int networkId, out string name)
    {
        return Current.NameByNetworkId.TryGetValue(networkId, out name!);
    }

    public static bool IsTool(int stackTypeId) => TryAsTool(stackTypeId, out _);

    public static int Require(string name)
    {
        if (Current.NetworkIdByName.TryGetValue(name, out var id))
            return id;
        throw new InvalidOperationException($"Curated tool missing '{name}'.");
    }

    public static IEnumerable<int> AllNetworkIds
    {
        get
        {
            return Current.ByNetworkId.Keys;
        }
    }
}

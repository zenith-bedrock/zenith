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
    private static readonly Dictionary<int, ToolInfo> ByNetworkId = new();
    private static readonly Dictionary<int, string> NameByNetworkId = new();
    private static readonly Dictionary<string, int> NetworkIdByName = new(StringComparer.Ordinal);
    private static bool _loaded;

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

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (LoadGate)
        {
            if (_loaded) return;
            LoadUnlocked(ItemPaletteLoader.FromEmbeddedResource());
        }
    }

    public static void Load(ItemPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        lock (LoadGate)
            LoadUnlocked(palette);
    }

    private static void LoadUnlocked(ItemPalette palette)
    {
        ByNetworkId.Clear();
        NameByNetworkId.Clear();
        NetworkIdByName.Clear();

        Register(palette, "minecraft:wooden_pickaxe", ToolKind.Pickaxe, ToolTier.Wood);
        Register(palette, "minecraft:wooden_axe", ToolKind.Axe, ToolTier.Wood);
        Register(palette, "minecraft:wooden_shovel", ToolKind.Shovel, ToolTier.Wood);
        Register(palette, "minecraft:stone_pickaxe", ToolKind.Pickaxe, ToolTier.Stone);
        Register(palette, "minecraft:stone_axe", ToolKind.Axe, ToolTier.Stone);
        Register(palette, "minecraft:stone_shovel", ToolKind.Shovel, ToolTier.Stone);
        Register(palette, "minecraft:iron_pickaxe", ToolKind.Pickaxe, ToolTier.Iron);
        Register(palette, "minecraft:iron_axe", ToolKind.Axe, ToolTier.Iron);
        Register(palette, "minecraft:iron_shovel", ToolKind.Shovel, ToolTier.Iron);
        Register(palette, "minecraft:diamond_pickaxe", ToolKind.Pickaxe, ToolTier.Diamond);
        Register(palette, "minecraft:diamond_axe", ToolKind.Axe, ToolTier.Diamond);
        Register(palette, "minecraft:diamond_shovel", ToolKind.Shovel, ToolTier.Diamond);
        _loaded = true;
    }

    internal static void ResetForTests()
    {
        lock (LoadGate)
        {
            _loaded = false;
            ByNetworkId.Clear();
            NameByNetworkId.Clear();
            NetworkIdByName.Clear();
        }
    }

    /// <summary>
    /// <paramref name="id"/> is checked against the table <see cref="LoadUnlocked"/> just cleared,
    /// not across reloads — <see cref="Load"/> re-running with a different <see cref="ItemPalette"/>
    /// is deliberate (matches <see cref="CreativeCatalog.CreateDefault(ItemPalette)"/>'s real call
    /// site), but two curated names resolving to the same network id within one pass is always a
    /// contributor mistake, never a valid state.
    /// </summary>
    private static void Register(ItemPalette palette, string name, ToolKind kind, ToolTier tier)
    {
        var id = palette.Require(name);
        if (!ByNetworkId.TryAdd(id, new ToolInfo(kind, tier)))
            throw new InvalidOperationException($"Duplicate tool network id {id} ('{name}' collides with '{NameByNetworkId[id]}').");
        NameByNetworkId[id] = name;
        NetworkIdByName[name] = id;
    }

    public static bool TryAsTool(int stackTypeId, out ToolInfo tool)
    {
        EnsureLoaded();
        if (ByNetworkId.TryGetValue(stackTypeId, out tool))
            return true;
        tool = ToolInfo.None;
        return false;
    }

    public static ToolInfo AsTool(int stackTypeId) =>
        TryAsTool(stackTypeId, out var tool) ? tool : ToolInfo.None;

    public static bool TryGetName(int networkId, out string name)
    {
        EnsureLoaded();
        return NameByNetworkId.TryGetValue(networkId, out name!);
    }

    public static bool IsTool(int stackTypeId) => TryAsTool(stackTypeId, out _);

    public static int Require(string name)
    {
        EnsureLoaded();
        if (NetworkIdByName.TryGetValue(name, out var id))
            return id;
        throw new InvalidOperationException($"Curated tool missing '{name}'.");
    }

    public static IEnumerable<int> AllNetworkIds
    {
        get
        {
            EnsureLoaded();
            return ByNetworkId.Keys;
        }
    }
}

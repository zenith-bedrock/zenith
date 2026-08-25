using System.Collections.Generic;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Creative palette SSOT (ADR §38/§140). Net ids reminted via CreativeContentPacket.
/// Blocks use block runtime ids; curated tools use item network ids (ADR §27).
///
/// Backing data is the real vanilla catalog (<see cref="CreativeItemsLoader"/>, ADR §140) — every
/// entry it offers is filtered through the same safety gate this catalog always used: a block is
/// exposed only if it has a <see cref="Blocks"/> name AND a <see cref="DigProfiles"/> entry; an item
/// is exposed only if it's one of <see cref="Tools"/>'s curated tools. This is unchanged from the
/// pre-§140 hand-typed registration — the win is real names/icons/vanilla groups instead of one fake
/// placeholder group, and every future <c>DigProfiles</c>/<c>Tools</c> addition is picked up here
/// automatically, no more hand-typed <c>RegisterBlock</c> call per item.
///
/// The 25 <c>const uint</c> below are stable wire net ids pinned to specific vanilla items, not a
/// sequential encoding of the real catalog's order — dozens of existing tests reference them as
/// known-good creative net ids (e.g. <c>CreativeCatalog.Stone</c> for exercising CraftCreative wire
/// flows). Preserving them keeps those tests meaningful without churn; any newly-eligible item that
/// isn't one of these 25 gets an auto-assigned net id above the pinned range.
/// </summary>
sealed class CreativeCatalog
{
    public const uint Stone = 1;
    public const uint GrassBlock = 2;
    public const uint Dirt = 3;
    public const uint OakPlanks = 4;
    public const uint OakLog = 5;
    public const uint Sand = 6;
    public const uint Chest = 7;
    public const uint Gravel = 20;
    public const uint Torch = 21;
    public const uint OakFence = 22;
    public const uint StoneBricks = 23;
    public const uint CobblestoneWall = 24;
    public const uint CraftingTable = 25;

    public const uint WoodenPickaxe = 8;
    public const uint WoodenAxe = 9;
    public const uint WoodenShovel = 10;
    public const uint StonePickaxe = 11;
    public const uint StoneAxe = 12;
    public const uint StoneShovel = 13;
    public const uint IronPickaxe = 14;
    public const uint IronAxe = 15;
    public const uint IronShovel = 16;
    public const uint DiamondPickaxe = 17;
    public const uint DiamondAxe = 18;
    public const uint DiamondShovel = 19;

    private const uint FirstAutoNetId = 26;

    private readonly Dictionary<uint, Entry> _byNetId = new();
    private readonly List<GroupSnapshot> _groups = new();
    private bool _frozen;

    readonly record struct Entry(uint NetId, int StackTypeId, int BaseCount, bool IsBlock, int GroupIndex);

    public readonly record struct CatalogSnapshot(uint NetId, int StackTypeId, int BaseCount, bool IsBlock, int GroupIndex);

    public readonly record struct GroupSnapshot(int Category, string Name, StackId Icon);

    public IReadOnlyList<GroupSnapshot> Groups => _groups;

    public static CreativeCatalog CreateDefault() =>
        CreateDefault(ItemPaletteLoader.FromEmbeddedResource());

    public static CreativeCatalog CreateDefault(ItemPalette itemPalette)
    {
        Blocks.EnsureLoaded();
        Tools.Load(itemPalette);
        DigProfiles.EnsureLoaded();

        var data = CreativeItemsLoader.FromEmbeddedResource(itemPalette);
        var catalog = new CreativeCatalog();
        var stableNetIds = BuildStableNetIdMap();
        var nextAutoNetId = FirstAutoNetId;
        var groupRemap = new Dictionary<int, int>();

        foreach (var item in data.Items)
        {
            if (!IsEligible(item.Id))
                continue;

            var netId = stableNetIds.TryGetValue(item.Id, out var pinned) ? pinned : nextAutoNetId++;
            var groupIndex = catalog.RemapGroup(item.GroupIndex, data.Groups, groupRemap);
            catalog.Register(netId, item.Id.Value, item.Id.IsBlock, groupIndex);
        }

        return catalog;
    }

    private static Dictionary<StackId, uint> BuildStableNetIdMap() => new()
    {
        [StackId.FromBlock(Blocks.Stone)] = Stone,
        [StackId.FromBlock(Blocks.GrassBlock)] = GrassBlock,
        [StackId.FromBlock(Blocks.Dirt)] = Dirt,
        [StackId.FromBlock(Blocks.OakPlanks)] = OakPlanks,
        [StackId.FromBlock(Blocks.OakLog)] = OakLog,
        [StackId.FromBlock(Blocks.Sand)] = Sand,
        [StackId.FromBlock(Blocks.Chest)] = Chest,
        [StackId.FromBlock(Blocks.Gravel)] = Gravel,
        [StackId.FromBlock(Blocks.Torch)] = Torch,
        [StackId.FromBlock(Blocks.OakFence)] = OakFence,
        [StackId.FromBlock(Blocks.StoneBricks)] = StoneBricks,
        [StackId.FromBlock(Blocks.CobblestoneWall)] = CobblestoneWall,
        [StackId.FromBlock(Blocks.CraftingTable)] = CraftingTable,
        [StackId.FromItem(Tools.Require("minecraft:wooden_pickaxe"))] = WoodenPickaxe,
        [StackId.FromItem(Tools.Require("minecraft:wooden_axe"))] = WoodenAxe,
        [StackId.FromItem(Tools.Require("minecraft:wooden_shovel"))] = WoodenShovel,
        [StackId.FromItem(Tools.Require("minecraft:stone_pickaxe"))] = StonePickaxe,
        [StackId.FromItem(Tools.Require("minecraft:stone_axe"))] = StoneAxe,
        [StackId.FromItem(Tools.Require("minecraft:stone_shovel"))] = StoneShovel,
        [StackId.FromItem(Tools.Require("minecraft:iron_pickaxe"))] = IronPickaxe,
        [StackId.FromItem(Tools.Require("minecraft:iron_axe"))] = IronAxe,
        [StackId.FromItem(Tools.Require("minecraft:iron_shovel"))] = IronShovel,
        [StackId.FromItem(Tools.Require("minecraft:diamond_pickaxe"))] = DiamondPickaxe,
        [StackId.FromItem(Tools.Require("minecraft:diamond_axe"))] = DiamondAxe,
        [StackId.FromItem(Tools.Require("minecraft:diamond_shovel"))] = DiamondShovel,
    };

    /// <summary>
    /// Safety gate (ADR §38, unchanged by §140): a block is exposed only once it has a real name
    /// AND a curated <see cref="DigProfiles"/> row; a pure item is exposed only if it's one of
    /// <see cref="Tools"/>'s curated tools. Keeps the ~1,800-item real catalog from exposing a
    /// multi-state block (stairs/slabs/doors) with no defined physics.
    /// </summary>
    private static bool IsEligible(StackId id) =>
        id.IsBlock
            ? Blocks.TryGetName(id.Value, out _) && DigProfiles.TryGet(id.Value, out _)
            : Tools.TryGetName(id.Value, out _);

    /// <summary>Re-indexes source group indices to a dense 0-based range covering only groups an eligible item actually references.</summary>
    private int RemapGroup(int sourceIndex, IReadOnlyList<CreativeGroupData> sourceGroups, Dictionary<int, int> remap)
    {
        if (remap.TryGetValue(sourceIndex, out var mapped))
            return mapped;

        var g = sourceGroups[sourceIndex];
        mapped = _groups.Count;
        _groups.Add(new GroupSnapshot(g.Category, g.Name, g.Icon));
        remap[sourceIndex] = mapped;
        return mapped;
    }

    private void Register(uint netId, int stackTypeId, bool isBlock, int groupIndex)
    {
        if (_frozen)
            throw new InvalidOperationException("CreativeCatalog is frozen; register before Freeze().");
        if (_byNetId.ContainsKey(netId))
            throw new InvalidOperationException($"Duplicate creative net id {netId}.");
        _byNetId[netId] = new Entry(netId, stackTypeId, 1, isBlock, groupIndex);
    }

    /// <summary>
    /// Marks composition complete — no further registration is accepted afterward. Called once by
    /// the composition root right after <see cref="CreateDefault(ItemPalette)"/>, before GameLoop
    /// starts ticking. Compose → freeze → gameplay reads only.
    /// </summary>
    internal void Freeze() => _frozen = true;

    public IReadOnlyList<CatalogSnapshot> SnapshotEntries()
    {
        var list = new List<CatalogSnapshot>(_byNetId.Count);
        foreach (var e in _byNetId.Values)
            list.Add(new CatalogSnapshot(e.NetId, e.StackTypeId, e.BaseCount, e.IsBlock, e.GroupIndex));
        list.Sort((a, b) => a.NetId.CompareTo(b.NetId));
        return list;
    }

    public bool TryGet(uint creativeNetId, out StackId id, out int baseCount)
    {
        if (!_byNetId.TryGetValue(creativeNetId, out var e))
        {
            id = default;
            baseCount = 0;
            return false;
        }

        id = e.IsBlock ? StackId.FromBlock(e.StackTypeId) : StackId.FromItem(e.StackTypeId);
        baseCount = e.BaseCount;
        return true;
    }
}

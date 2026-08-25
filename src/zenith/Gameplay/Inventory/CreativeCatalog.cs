using System.Collections.Generic;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Creative palette SSOT (ADR §38). Net ids reminted via CreativeContentPacket.
/// Blocks use block runtime ids; curated tools use item network ids (ADR §27).
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

    private readonly Dictionary<uint, Entry> _byNetId = new();
    private bool _frozen;

    readonly record struct Entry(uint NetId, int StackTypeId, int BaseCount, bool IsBlock);

    public readonly record struct CatalogSnapshot(uint NetId, int StackTypeId, int BaseCount, bool IsBlock);

    public static CreativeCatalog CreateDefault() =>
        CreateDefault(ItemPaletteLoader.FromEmbeddedResource());

    public static CreativeCatalog CreateDefault(ItemPalette itemPalette)
    {
        Blocks.EnsureLoaded();
        Tools.Load(itemPalette);
        var catalog = new CreativeCatalog();
        catalog.RegisterBlock(Stone, Blocks.Stone);
        catalog.RegisterBlock(GrassBlock, Blocks.GrassBlock);
        catalog.RegisterBlock(Dirt, Blocks.Dirt);
        catalog.RegisterBlock(OakPlanks, Blocks.OakPlanks);
        catalog.RegisterBlock(OakLog, Blocks.OakLog);
        catalog.RegisterBlock(Sand, Blocks.Sand);
        catalog.RegisterBlock(Chest, Blocks.Chest);
        catalog.RegisterBlock(Gravel, Blocks.Gravel);
        catalog.RegisterBlock(Torch, Blocks.Torch);
        catalog.RegisterBlock(OakFence, Blocks.OakFence);
        catalog.RegisterBlock(StoneBricks, Blocks.StoneBricks);
        catalog.RegisterBlock(CobblestoneWall, Blocks.CobblestoneWall);

        catalog.RegisterTool(WoodenPickaxe, Tools.Require("minecraft:wooden_pickaxe"));
        catalog.RegisterTool(WoodenAxe, Tools.Require("minecraft:wooden_axe"));
        catalog.RegisterTool(WoodenShovel, Tools.Require("minecraft:wooden_shovel"));
        catalog.RegisterTool(StonePickaxe, Tools.Require("minecraft:stone_pickaxe"));
        catalog.RegisterTool(StoneAxe, Tools.Require("minecraft:stone_axe"));
        catalog.RegisterTool(StoneShovel, Tools.Require("minecraft:stone_shovel"));
        catalog.RegisterTool(IronPickaxe, Tools.Require("minecraft:iron_pickaxe"));
        catalog.RegisterTool(IronAxe, Tools.Require("minecraft:iron_axe"));
        catalog.RegisterTool(IronShovel, Tools.Require("minecraft:iron_shovel"));
        catalog.RegisterTool(DiamondPickaxe, Tools.Require("minecraft:diamond_pickaxe"));
        catalog.RegisterTool(DiamondAxe, Tools.Require("minecraft:diamond_axe"));
        catalog.RegisterTool(DiamondShovel, Tools.Require("minecraft:diamond_shovel"));
        return catalog;
    }

    /// <summary>Boot-time-only; <see cref="CreateDefault(ItemPalette)"/> is still the only caller.</summary>
    private void RegisterBlock(uint netId, int blockRuntimeId) => Register(netId, blockRuntimeId, isBlock: true);

    /// <summary>Boot-time-only; <see cref="CreateDefault(ItemPalette)"/> is still the only caller.</summary>
    private void RegisterTool(uint netId, int itemNetworkId) => Register(netId, itemNetworkId, isBlock: false);

    private void Register(uint netId, int stackTypeId, bool isBlock)
    {
        if (_frozen)
            throw new InvalidOperationException("CreativeCatalog is frozen; register before Freeze().");
        if (_byNetId.ContainsKey(netId))
            throw new InvalidOperationException($"Duplicate creative net id {netId}.");
        _byNetId[netId] = new Entry(netId, stackTypeId, 1, isBlock);
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
            list.Add(new CatalogSnapshot(e.NetId, e.StackTypeId, e.BaseCount, e.IsBlock));
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

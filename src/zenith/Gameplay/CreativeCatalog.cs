using System.Collections.Generic;
using Zenith.World;

namespace Zenith.Gameplay;

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

    private void RegisterBlock(uint netId, int blockRuntimeId) =>
        _byNetId[netId] = new Entry(netId, blockRuntimeId, 1, IsBlock: true);

    private void RegisterTool(uint netId, int itemNetworkId) =>
        _byNetId[netId] = new Entry(netId, itemNetworkId, 1, IsBlock: false);

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

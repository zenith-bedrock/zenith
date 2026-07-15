using System.Collections.Generic;
using Zenith.World;

namespace Zenith.Gameplay;

/// <summary>
/// Creative palette SSOT (ADR §38). Net ids reminted via CreativeContentPacket.
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

    private readonly Dictionary<uint, Entry> _byNetId = new();

    readonly record struct Entry(uint NetId, int RuntimeId, int BaseCount);

    public readonly record struct CatalogSnapshot(uint NetId, int RuntimeId, int BaseCount);

    public static CreativeCatalog CreateDefault()
    {
        Blocks.EnsureLoaded();
        var catalog = new CreativeCatalog();
        catalog.Register(Stone, Blocks.Stone);
        catalog.Register(GrassBlock, Blocks.GrassBlock);
        catalog.Register(Dirt, Blocks.Dirt);
        catalog.Register(OakPlanks, Blocks.OakPlanks);
        catalog.Register(OakLog, Blocks.OakLog);
        catalog.Register(Sand, Blocks.Sand);
        catalog.Register(Chest, Blocks.Chest);
        return catalog;
    }

    private void Register(uint netId, int runtimeId) =>
        _byNetId[netId] = new Entry(netId, runtimeId, 1);

    public IReadOnlyList<CatalogSnapshot> SnapshotEntries()
    {
        var list = new List<CatalogSnapshot>(_byNetId.Count);
        foreach (var e in _byNetId.Values)
            list.Add(new CatalogSnapshot(e.NetId, e.RuntimeId, e.BaseCount));
        list.Sort((a, b) => a.NetId.CompareTo(b.NetId));
        return list;
    }

    public bool TryGet(uint creativeNetId, out int runtimeId, out int baseCount)
    {
        if (!_byNetId.TryGetValue(creativeNetId, out var e))
        {
            runtimeId = Blocks.Air;
            baseCount = 0;
            return false;
        }

        runtimeId = e.RuntimeId;
        baseCount = e.BaseCount;
        return true;
    }
}

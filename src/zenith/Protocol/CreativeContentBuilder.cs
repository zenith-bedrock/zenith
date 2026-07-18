using Zenith.Gameplay;
using Zenith.Packets;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Builds CreativeContentPacket from <see cref="CreativeCatalog"/> SSOT (ADR §38 / §54).</summary>
static class CreativeContentBuilder
{
    /// <summary>Wire DTOs from <see cref="CreativeCatalog"/> SSOT (ADR §38).</summary>
    public static CreativeContentPacket Build(CreativeCatalog catalog, ItemPalette palette)
    {
        var snapshots = catalog.SnapshotEntries();
        var items = new CreativeItemEntry[snapshots.Count];
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snap = snapshots[i];
            if (!Blocks.TryGetName(snap.RuntimeId, out var name))
                throw new InvalidOperationException($"CreativeContent: unknown runtime {snap.RuntimeId}.");
            items[i] = new CreativeItemEntry(
                snap.NetId,
                new NetworkItemStack(palette.Require(name), (ushort)snap.BaseCount, snap.RuntimeId),
                GroupIndex: 0);
        }

        return new CreativeContentPacket
        {
            Groups =
            [
                new CreativeGroupEntry(
                    CreativeContentPacket.CategoryConstruction,
                    Name: "",
                    Icon: NetworkItemStack.Empty)
            ],
            Items = items
        };
    }

}

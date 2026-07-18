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
            string name;
            int wireBlockRid;
            short networkId;
            if (snap.IsBlock)
            {
                if (!Blocks.TryGetName(snap.StackTypeId, out name!))
                    throw new InvalidOperationException($"CreativeContent: unknown block runtime {snap.StackTypeId}.");
                networkId = palette.Require(name);
                wireBlockRid = snap.StackTypeId;
            }
            else
            {
                if (!Tools.TryGetName(snap.StackTypeId, out name!))
                    throw new InvalidOperationException($"CreativeContent: unknown tool network id {snap.StackTypeId}.");
                networkId = palette.Require(name);
                wireBlockRid = 0;
            }

            items[i] = new CreativeItemEntry(
                snap.NetId,
                new NetworkItemStack(networkId, (ushort)snap.BaseCount, wireBlockRid),
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

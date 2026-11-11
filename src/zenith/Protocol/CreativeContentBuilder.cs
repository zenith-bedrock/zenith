using Zenith.Packets;
using Zenith.World;
using Zenith.Gameplay.Inventory;

namespace Zenith.Protocol;

/// <summary>Builds CreativeContentPacket from <see cref="CreativeCatalog"/> SSOT (ADR §38 / §54 / §142).</summary>
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
                GroupIndex: (uint)snap.GroupIndex);
        }

        var catalogGroups = catalog.Groups;
        var groups = new CreativeGroupEntry[catalogGroups.Count];
        for (var i = 0; i < catalogGroups.Count; i++)
        {
            var g = catalogGroups[i];
            groups[i] = new CreativeGroupEntry(g.Category, g.Name, ResolveIcon(g.Icon, palette));
        }

        return new CreativeContentPacket { Groups = groups, Items = items };
    }

    /// <summary>
    /// Group icons are display-only, so resolution is deliberately looser than an item entry's:
    /// any name the block palette / item palette already knows works, with no <see cref="DigProfiles"/>
    /// or <see cref="Tools"/> gate. An empty <see cref="StackId"/> (real vanilla groups use a
    /// "minecraft:empty" sentinel — client falls back to the group's first item) maps to
    /// <see cref="NetworkItemStack.Empty"/>, matching what this builder already sent before ADR §142.
    /// </summary>
    private static NetworkItemStack ResolveIcon(StackId icon, ItemPalette palette)
    {
        if (icon.IsEmpty)
            return NetworkItemStack.Empty;

        if (icon.IsBlock)
        {
            return Blocks.TryGetName(icon.Value, out var name)
                ? new NetworkItemStack(palette.Require(name), 1, icon.Value)
                : NetworkItemStack.Empty;
        }

        return palette.TryGetName(icon.Value, out var itemName)
            ? new NetworkItemStack(palette.Require(itemName), 1, 0)
            : NetworkItemStack.Empty;
    }
}

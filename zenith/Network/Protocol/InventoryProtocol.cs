using System.Collections.Generic;
using Zenith.Network.Packets;
using Zenith.Network.Session;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Network.Protocol;

/// <summary>Transmite conteúdo de inventário e ItemRegistry — mapping wire no boundary.</summary>
sealed class InventoryProtocol
{
    private const int MaxUnknownBlockWarns = 64;

    private readonly NetworkSession _session;
    private readonly HashSet<int> _warnedUnknownBlocks = new();

    public InventoryProtocol(NetworkSession session) => _session = session;

    public void SendItemRegistry()
    {
        _session.SendDataPacket(new ItemRegistryPacket
        {
            Entries = _session.Context.ItemPalette.Entries
        });
    }

    public void SendHotbarContent(PlayerInventory inventory)
    {
        var slots = inventory.SnapshotMainInventory();
        var wire = new NetworkItemStack[slots.Length];
        for (var i = 0; i < slots.Length; i++)
            wire[i] = ToNetworkStack(slots[i]);

        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            Slots = wire
        });
    }

    private NetworkItemStack ToNetworkStack(InventorySlot slot)
    {
        if (slot.IsEmpty)
            return NetworkItemStack.Empty;

        var palette = _session.Context.ItemPalette;
        if (Blocks.TryGetName(slot.RuntimeId, out var name) && palette.TryGet(name, out var networkId))
            return new NetworkItemStack(networkId, (ushort)slot.Count, slot.RuntimeId);

        WarnUnknownBlockOnce(slot.RuntimeId);
        var airId = palette.Require("minecraft:air");
        return new NetworkItemStack(airId, 0, Blocks.Air);
    }

    private void WarnUnknownBlockOnce(int runtimeId)
    {
        if (_warnedUnknownBlocks.Count >= MaxUnknownBlockWarns)
            return;
        if (!_warnedUnknownBlocks.Add(runtimeId))
            return;
        _session.Context.Logger.Warning(
            $"Unknown block runtime id {runtimeId} in inventory wire map; sending air item id (cap {MaxUnknownBlockWarns}).");
    }
}

using System.Collections.Generic;
using Zenith.Network.Packets;
using Zenith.Network.Session;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Network.Protocol;

/// <summary>
/// Wire map inventário: container IDs Bedrock → flat 0–35 / cursor.
/// Sem lógica de gameplay — só boundary.
/// </summary>
static class InventoryContainerMap
{
    public const byte CombinedHotbarAndInventory = 12;
    public const byte Hotbar = 28;
    public const byte Inventory = 29;
    public const byte Cursor = 59;

    public static bool TryMap(byte containerId, byte slot, out int flat)
    {
        switch (containerId)
        {
            case Hotbar:
            case CombinedHotbarAndInventory:
                if (slot >= PlayerInventory.HotbarSize)
                {
                    flat = 0;
                    return false;
                }

                flat = slot;
                return true;

            case Inventory:
                flat = slot + PlayerInventory.HotbarSize;
                if (!PlayerInventory.IsValidInventorySlot(flat))
                {
                    flat = 0;
                    return false;
                }

                return true;

            case Cursor:
                flat = PlayerInventory.CursorSlot;
                return true;

            default:
                flat = 0;
                return false;
        }
    }

    public static bool TryToWire(int flat, out byte containerId, out byte wireSlot)
    {
        if (flat == PlayerInventory.CursorSlot)
        {
            containerId = Cursor;
            wireSlot = 0;
            return true;
        }

        if (flat is >= 0 and < PlayerInventory.HotbarSize)
        {
            containerId = Hotbar;
            wireSlot = (byte)flat;
            return true;
        }

        if (PlayerInventory.IsValidInventorySlot(flat))
        {
            containerId = Inventory;
            wireSlot = (byte)(flat - PlayerInventory.HotbarSize);
            return true;
        }

        containerId = 0;
        wireSlot = 0;
        return false;
    }
}

/// <summary>Transmite inventário / ISR responses — net IDs e mapping wire no boundary.</summary>
sealed class InventoryProtocol
{
    private const int MaxUnknownBlockWarns = 64;

    private readonly NetworkSession _session;
    private readonly HashSet<int> _warnedUnknownBlocks = new();
    private readonly int[] _slotNetIds = new int[PlayerInventory.FullInventorySize];
    private int _cursorNetId;
    private int _nextNetId = 1;

    public InventoryProtocol(NetworkSession session) => _session = session;

    public void SendItemRegistry()
    {
        _session.SendDataPacket(new ItemRegistryPacket
        {
            Entries = _session.Context.ItemPalette.Entries
        });
    }

    public void SendInventoryContent(PlayerInventory inventory)
    {
        var slots = inventory.SnapshotMainInventory();
        var wire = new NetworkItemStack[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            RefreshNetId(i, slots[i]);
            wire[i] = ToNetworkStack(slots[i], _slotNetIds[i]);
        }

        RefreshNetId(PlayerInventory.CursorSlot, inventory.Cursor);

        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            Slots = wire
        });
    }

    public void SendContainerOpen(int blockX, int blockY, int blockZ)
    {
        _session.SendDataPacket(new ContainerOpenPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            WindowType = ContainerOpenPacket.WindowTypeInventory,
            BlockX = blockX,
            BlockY = blockY,
            BlockZ = blockZ,
            ActorUniqueId = -1
        });
    }

    public void SendContainerClose(byte windowId, byte windowType)
    {
        _session.SendDataPacket(new ContainerClosePacket
        {
            WindowId = windowId,
            WindowType = windowType,
            ServerInitiated = false
        });
    }

    public void SendItemStackResponseError(int requestId) =>
        _session.SendDataPacket(ItemStackResponsePacket.Error(requestId));

    public void SendItemStackResponseOk(int requestId, PlayerInventory inventory, IReadOnlyList<int> touchedFlats)
    {
        var byContainer = new Dictionary<byte, List<StackResponseSlotInfo>>();
        foreach (var flat in touchedFlats)
        {
            if (!InventoryContainerMap.TryToWire(flat, out var containerId, out var wireSlot))
                continue;

            RefreshNetId(flat, inventory.Get(flat));
            var stack = inventory.Get(flat);
            var netId = GetNetId(flat);
            var info = new StackResponseSlotInfo
            {
                Slot = wireSlot,
                HotbarSlot = wireSlot,
                Count = (byte)Math.Clamp(stack.IsEmpty ? 0 : stack.Count, 0, 255),
                StackNetworkId = netId
            };

            if (!byContainer.TryGetValue(containerId, out var list))
            {
                list = new List<StackResponseSlotInfo>();
                byContainer[containerId] = list;
            }

            list.Add(info);
        }

        var containers = new StackResponseContainerInfo[byContainer.Count];
        var i = 0;
        foreach (var (containerId, slots) in byContainer)
        {
            containers[i++] = new StackResponseContainerInfo
            {
                Container = new FullContainerName { ContainerId = containerId },
                SlotInfo = slots.ToArray()
            };
        }

        _session.SendDataPacket(ItemStackResponsePacket.Ok(requestId, containers));
    }

    private void RefreshNetId(int flat, InventorySlot slot)
    {
        if (slot.IsEmpty)
        {
            SetNetId(flat, 0);
            return;
        }

        SetNetId(flat, AllocateNetId());
    }

    private int AllocateNetId() => _nextNetId++;

    private int GetNetId(int flat) =>
        flat == PlayerInventory.CursorSlot ? _cursorNetId : _slotNetIds[flat];

    private void SetNetId(int flat, int netId)
    {
        if (flat == PlayerInventory.CursorSlot)
            _cursorNetId = netId;
        else
            _slotNetIds[flat] = netId;
    }

    private NetworkItemStack ToNetworkStack(InventorySlot slot, int stackNetworkId)
    {
        if (slot.IsEmpty)
            return NetworkItemStack.Empty;

        var palette = _session.Context.ItemPalette;
        if (Blocks.TryGetName(slot.RuntimeId, out var name) && palette.TryGet(name, out var networkId))
            return new NetworkItemStack(networkId, (ushort)slot.Count, slot.RuntimeId, StackNetworkId: stackNetworkId);

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

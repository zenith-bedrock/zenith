using System.Collections.Generic;
using Zenith.Gameplay;
using Zenith.Packets;
using Zenith.Session;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Transmite inventário / ISR responses — façade over builders + <see cref="InventoryNetIds"/> (§54).</summary>
sealed class InventoryProtocol
{
    private const int MaxUnknownBlockWarns = 64;

    private readonly NetworkSession _session;
    private readonly HashSet<int> _warnedUnknownBlocks = new();
    private readonly InventoryNetIds _netIds = new();

    public InventoryProtocol(NetworkSession session) => _session = session;

    public void SendItemRegistry()
    {
        var palette = _session.Context.ItemPalette.Entries;
        var wire = new ItemRegistryWireEntry[palette.Count];
        for (var i = 0; i < palette.Count; i++)
        {
            var e = palette[i];
            wire[i] = new ItemRegistryWireEntry(e.Name, e.NetworkId, e.Version, e.ComponentBased);
        }

        _session.SendDataPacket(new ItemRegistryPacket { Entries = wire });
    }

    public void SendCreativeContent()
    {
        _session.SendDataPacket(CreativeContentBuilder.Build(_session.Context.Creative, _session.Context.ItemPalette));
    }

    public void SendCraftingData() =>
        _session.SendDataPacket(CraftingDataBuilder.Build(_session.Context.Recipes, _session.Context.ItemPalette));


    /// <summary>Façade — see <see cref="CreativeContentBuilder"/>.</summary>
    internal static CreativeContentPacket BuildCreativeContent(CreativeCatalog catalog, ItemPalette palette) =>
        CreativeContentBuilder.Build(catalog, palette);

    /// <summary>Façade — see <see cref="CraftingDataBuilder"/>.</summary>
    internal static CraftingDataPacket BuildCraftingData(RecipeRegistry registry, ItemPalette palette) =>
        CraftingDataBuilder.Build(registry, palette);


    public void SendInventoryContent(PlayerInventory inventory)
    {
        var slots = inventory.SnapshotMainInventory();
        var wire = new NetworkItemStack[slots.Length];
        for (var i = 0; i < slots.Length; i++)
            wire[i] = DescribeForWire(i, slots[i]);

        // Keep cursor advertisement warm even though window 0 payload is bag-only.
        _ = DescribeForWire(PlayerInventory.CursorSlot, inventory.Cursor);

        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            Slots = wire
        });
    }

    /// <summary>Window 124 — 54-slot UI inventory (cursor at 0, craft grid 28–31, result 50).</summary>
    public void SendUiInventoryContent(global::Zenith.Player.Player player)
    {
        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowUI,
            Slots = BuildUiInventorySlots(player)
        });
    }

    /// <summary>
    /// Window 124 (player-only UI) slot map — cursor at Mojang PlayerUISlot 0.
    /// Omitting slot 0 wiped ISR cursor after OK (smoke 11).
    /// </summary>
    internal NetworkItemStack[] BuildUiInventorySlots(global::Zenith.Player.Player player)
    {
        var wire = new NetworkItemStack[InventoryContainerMap.UiInventorySlotCount];
        for (var i = 0; i < wire.Length; i++)
            wire[i] = NetworkItemStack.Empty;

        wire[InventoryContainerMap.UiCursorSlot] =
            DescribeForWire(PlayerInventory.CursorSlot, player.Inventory.Cursor);

        for (var g = 0; g < PlayerCraftUi.GridSize; g++)
        {
            var flat = InventoryContainerMap.CraftUiBase + g;
            wire[InventoryContainerMap.CraftingGridWireOffset + g] =
                DescribeForWire(flat, player.CraftUi.GetGrid(g));
        }

        wire[InventoryContainerMap.CraftingResultWireSlot] =
            DescribeForWire(InventoryContainerMap.CraftResultFlat, player.CraftUi.Result);

        return wire;
    }

    public void SendChestContent(ChestStore chests, int x, int y, int z)
    {
        chests.Ensure(x, y, z);
        if (!chests.TryGetSlots(x, y, z, out var slots))
            return;

        var wire = new NetworkItemStack[ChestStore.Size];
        for (var i = 0; i < ChestStore.Size; i++)
        {
            var flat = InventoryContainerMap.ChestBase + i;
            wire[i] = DescribeForWire(flat, slots[i]);
        }

        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowChest,
            Slots = wire
        });
    }

    /// <summary>
    /// Single path for InventoryContent / UI / chest / ISR OK: refresh advertisement then
    /// build wire DTO. Prefer this over separate Refresh+Get (ADR §54).
    /// </summary>
    public NetworkItemStack DescribeForWire(int flat, InventorySlot slot)
    {
        var netId = _netIds.Refresh(flat, slot);
        return ToNetworkStack(slot, netId);
    }

    /// <summary>
    /// Soft ISR match vs last <see cref="DescribeForWire"/> advertisement.
    /// <paramref name="clientStackNetId"/> ≤ 0 → accept (air / prediction deferred).
    /// Positive mismatch → false (caller rejects + resync).
    /// </summary>
    public bool MatchesAdvertisedStackNetId(int flat, int clientStackNetId)
    {
        if (clientStackNetId <= 0)
            return true;
        return _netIds.Peek(flat) == clientStackNetId;
    }

    /// <summary>Peer display / AddPlayer — no stack net id allocation.</summary>
    public NetworkItemStack DescribeSlot(PlayerInventory inventory, int slot)
    {
        var stack = inventory.Get(slot);
        return ToNetworkStack(stack, stackNetworkId: 0);
    }

    /// <summary>Floor-drop / AddItemActor — domain runtime id + count → wire stack.</summary>
    public NetworkItemStack DescribeStack(int blockRuntimeId, int count)
    {
        if (count <= 0 || blockRuntimeId == Blocks.Air)
            return NetworkItemStack.Empty;
        return ToNetworkStack(new InventorySlot(blockRuntimeId, count), stackNetworkId: 0);
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

    public void SendChestOpen(int blockX, int blockY, int blockZ)
    {
        _session.SendDataPacket(new ContainerOpenPacket
        {
            WindowId = (byte)InventoryContentPacket.WindowChest,
            WindowType = ContainerOpenPacket.WindowTypeChest,
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

    public void SendItemStackResponseOk(int requestId, global::Zenith.Player.Player player, IReadOnlyList<WireTouch> wireTouches)
    {
        var byContainer = new Dictionary<byte, List<StackResponseSlotInfo>>();
        foreach (var touch in wireTouches)
        {
            // Always include CreatedOutput (60) in OK — omitting it desyncs sequential craft take.
            var stack = ResolveStack(player, touch.Flat);
            var wire = DescribeForWire(touch.Flat, stack);
            var info = new StackResponseSlotInfo
            {
                Slot = touch.Slot,
                HotbarSlot = touch.Slot,
                Count = (byte)Math.Clamp(stack.IsEmpty ? 0 : stack.Count, 0, 255),
                StackNetworkId = wire.StackNetworkId
            };

            if (!byContainer.TryGetValue(touch.ContainerId, out var list))
            {
                list = new List<StackResponseSlotInfo>();
                byContainer[touch.ContainerId] = list;
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

    /// <summary>Legacy flat-only path — maps via TryToWire (closed inventory / hotbar).</summary>
    public void SendItemStackResponseOk(int requestId, global::Zenith.Player.Player player, IReadOnlyList<int> touchedFlats)
    {
        var touches = new List<WireTouch>(touchedFlats.Count);
        foreach (var flat in touchedFlats)
        {
            if (!InventoryContainerMap.TryToWire(flat, out var containerId, out var wireSlot))
                continue;
            touches.Add(new WireTouch(flat, containerId, wireSlot));
        }

        SendItemStackResponseOk(requestId, player, touches);
    }

    private InventorySlot ResolveStack(global::Zenith.Player.Player player, int flat)
    {
        if (InventoryContainerMap.IsChestFlat(flat))
        {
            if (player.OpenChest is not { } pos) return InventorySlot.Empty;
            return _session.Context.World.Chests.Get(
                pos.X, pos.Y, pos.Z, flat - InventoryContainerMap.ChestBase);
        }

        if (InventoryContainerMap.IsCraftGridFlat(flat))
            return player.CraftUi.GetGrid(flat - InventoryContainerMap.CraftUiBase);

        if (flat == InventoryContainerMap.CraftResultFlat)
            return player.CraftUi.Result;

        return player.Inventory.Get(flat);
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

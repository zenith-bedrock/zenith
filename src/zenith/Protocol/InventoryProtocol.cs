using System.Collections.Generic;
using Zenith.Gameplay;
using Zenith.Packets;
using Zenith.Session;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>
/// Wire map inventário: container IDs Bedrock → flat 0–35 / cursor / chest 100+.
/// Sem lógica de gameplay — só boundary.
/// </summary>
static class InventoryContainerMap
{
    public const byte CombinedHotbarAndInventory = 12;
    /// <summary>Bedrock crafting input (2×2 / 3×3 UI) — domain flats CraftUiBase+.</summary>
    public const byte CraftingInput = 13;
    /// <summary>Bedrock crafting output preview.</summary>
    public const byte CraftingOutputPreview = 14;
    public const byte Hotbar = 28;
    public const byte Inventory = 29;
    public const byte Cursor = 59;
    /// <summary>Bedrock created-output (craft result pickup).</summary>
    public const byte CreatedOutput = 60;
    public const byte Chest = 7;

    /// <summary>Flat domínio para slots do baú aberto (não vive em <see cref="PlayerInventory"/>).</summary>
    public const int ChestBase = 100;

    /// <summary>Flat domínio craft UI 2×2 (ephemeral, ADR §35).</summary>
    public const int CraftUiBase = 150;

    /// <summary>Created output slot (container 60 wire slot 50).</summary>
    public const int CraftResultFlat = CraftUiBase + PlayerCraftUi.GridSize;

    public const int CraftingGridWireOffset = 28;
    public const byte CraftingResultWireSlot = 50;
    public const int UiInventorySlotCount = 54;

    /// <summary>Mojang PlayerUISlot::CURSOR — window 124 slot 0 (ISR container 59).</summary>
    public const int UiCursorSlot = 0;

    public static bool IsChestFlat(int flat) =>
        flat is >= ChestBase and < ChestBase + ChestStore.Size;

    public static bool IsCraftUiFlat(int flat) =>
        flat is >= CraftUiBase and <= CraftResultFlat;

    public static bool IsCraftGridFlat(int flat) =>
        flat is >= CraftUiBase and < CraftResultFlat;

    public static byte CraftGridWireSlot(int gridIndex) =>
        (byte)(CraftingGridWireOffset + gridIndex);

    public static bool TryMap(byte containerId, byte slot, out int flat)
    {
        switch (containerId)
        {
            case Hotbar:
                if (slot >= PlayerInventory.HotbarSize)
                {
                    flat = 0;
                    return false;
                }

                flat = slot;
                return true;

            case CombinedHotbarAndInventory:
            case Inventory:
                // Containers 12 and 29: slot is absolute bag index 0–35 (not “first inventory = 0”).
                if (slot >= PlayerInventory.FullInventorySize)
                {
                    flat = 0;
                    return false;
                }

                flat = slot;
                return true;

            case Cursor:
                flat = PlayerInventory.CursorSlot;
                return true;

            case Chest:
                if (slot >= ChestStore.Size)
                {
                    flat = 0;
                    return false;
                }

                flat = ChestBase + slot;
                return true;

            case CraftingInput:
                if (slot is >= CraftingGridWireOffset and < CraftingGridWireOffset + PlayerCraftUi.GridSize)
                {
                    flat = CraftUiBase + (slot - CraftingGridWireOffset);
                    return true;
                }

                flat = 0;
                return false;

            case CraftingOutputPreview:
            case CreatedOutput:
                if (slot == CraftingResultWireSlot)
                {
                    flat = CraftResultFlat;
                    return true;
                }

                flat = 0;
                return false;

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

        if (IsChestFlat(flat))
        {
            containerId = Chest;
            wireSlot = (byte)(flat - ChestBase);
            return true;
        }

        if (IsCraftGridFlat(flat))
        {
            containerId = CraftingInput;
            wireSlot = CraftGridWireSlot(flat - CraftUiBase);
            return true;
        }

        if (flat == CraftResultFlat)
        {
            containerId = CreatedOutput;
            wireSlot = CraftingResultWireSlot;
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
            // Container 29 slot = absolute bag index (0–35), not hotbar-relative 0–8.
            containerId = Inventory;
            wireSlot = (byte)flat;
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
    private readonly int[] _chestNetIds = new int[ChestStore.Size];
    private readonly int[] _craftNetIds = new int[PlayerCraftUi.GridSize + 1];
    private readonly Dictionary<int, (int RuntimeId, int Count)> _stackIdentity = new();
    private int _cursorNetId;
    private int _nextNetId = 1;

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
        _session.SendDataPacket(BuildCreativeContent(_session.Context.Creative, _session.Context.ItemPalette));
    }

    /// <summary>Wire DTOs from <see cref="CreativeCatalog"/> SSOT (ADR §38).</summary>
    internal static CreativeContentPacket BuildCreativeContent(CreativeCatalog catalog, ItemPalette palette)
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

    public void SendCraftingData() =>
        _session.SendDataPacket(BuildCraftingData(_session.Context.Recipes, _session.Context.ItemPalette));

    /// <summary>Wire DTOs from <see cref="RecipeRegistry"/> SSOT (ADR §35) — no second recipe table.</summary>
    internal static CraftingDataPacket BuildCraftingData(RecipeRegistry registry, ItemPalette palette)
    {
        var snapshots = registry.SnapshotRecipes();
        var recipes = new ShapelessCraftingRecipe[snapshots.Count];
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snap = snapshots[i];
            var inputs = new DefaultDescriptorInput[snap.Inputs.Length];
            for (var j = 0; j < snap.Inputs.Length; j++)
            {
                var (runtimeId, count) = snap.Inputs[j];
                if (!Blocks.TryGetName(runtimeId, out var inName))
                    throw new InvalidOperationException($"CraftingData: unknown input runtime {runtimeId}.");
                inputs[j] = new DefaultDescriptorInput(palette.Require(inName), 0, count);
            }

            if (!Blocks.TryGetName(snap.OutRuntimeId, out var outName))
                throw new InvalidOperationException($"CraftingData: unknown output runtime {snap.OutRuntimeId}.");

            recipes[i] = new ShapelessCraftingRecipe
            {
                RecipeId = $"zenith:recipe_{snap.NetId}",
                Inputs = inputs,
                Outputs =
                [
                    new NetworkItemStack(palette.Require(outName), (ushort)snap.OutCount, snap.OutRuntimeId)
                ],
                RecipeNetworkId = snap.NetId
            };
        }

        return new CraftingDataPacket
        {
            Recipes = recipes,
            ClearRecipes = true
        };
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

        RefreshNetId(PlayerInventory.CursorSlot, player.Inventory.Cursor);
        wire[InventoryContainerMap.UiCursorSlot] =
            ToNetworkStack(player.Inventory.Cursor, GetNetId(PlayerInventory.CursorSlot));

        for (var g = 0; g < PlayerCraftUi.GridSize; g++)
        {
            var flat = InventoryContainerMap.CraftUiBase + g;
            var stack = player.CraftUi.GetGrid(g);
            RefreshNetId(flat, stack);
            wire[InventoryContainerMap.CraftingGridWireOffset + g] =
                ToNetworkStack(stack, GetNetId(flat));
        }

        {
            var flat = InventoryContainerMap.CraftResultFlat;
            var stack = player.CraftUi.Result;
            RefreshNetId(flat, stack);
            wire[InventoryContainerMap.CraftingResultWireSlot] =
                ToNetworkStack(stack, GetNetId(flat));
        }

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
            RefreshNetId(flat, slots[i]);
            wire[i] = ToNetworkStack(slots[i], _chestNetIds[i]);
        }

        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowChest,
            Slots = wire
        });
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
            RefreshNetId(touch.Flat, stack);
            var netId = GetNetId(touch.Flat);
            var info = new StackResponseSlotInfo
            {
                Slot = touch.Slot,
                HotbarSlot = touch.Slot,
                Count = (byte)Math.Clamp(stack.IsEmpty ? 0 : stack.Count, 0, 255),
                StackNetworkId = netId
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

    private void RefreshNetId(int flat, InventorySlot slot)
    {
        if (slot.IsEmpty)
        {
            SetNetId(flat, 0);
            _stackIdentity.Remove(flat);
            return;
        }

        if (_stackIdentity.TryGetValue(flat, out var prev) &&
            prev.RuntimeId == slot.RuntimeId && prev.Count == slot.Count)
            return;

        SetNetId(flat, AllocateNetId());
        _stackIdentity[flat] = (slot.RuntimeId, slot.Count);
    }

    private int AllocateNetId() => _nextNetId++;

    private int GetNetId(int flat)
    {
        if (flat == PlayerInventory.CursorSlot) return _cursorNetId;
        if (InventoryContainerMap.IsChestFlat(flat))
            return _chestNetIds[flat - InventoryContainerMap.ChestBase];
        if (InventoryContainerMap.IsCraftUiFlat(flat))
            return _craftNetIds[flat - InventoryContainerMap.CraftUiBase];
        return _slotNetIds[flat];
    }

    private void SetNetId(int flat, int netId)
    {
        if (flat == PlayerInventory.CursorSlot)
            _cursorNetId = netId;
        else if (InventoryContainerMap.IsChestFlat(flat))
            _chestNetIds[flat - InventoryContainerMap.ChestBase] = netId;
        else if (InventoryContainerMap.IsCraftUiFlat(flat))
            _craftNetIds[flat - InventoryContainerMap.CraftUiBase] = netId;
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

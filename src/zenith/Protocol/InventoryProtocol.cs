using System.Collections.Generic;
using Zenith.Packets;
using Zenith.Session;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Inventory;

namespace Zenith.Protocol;

/// <summary>Transmite inventário / ISR responses — façade over builders + <see cref="InventoryNetIds"/> (§54).</summary>
sealed class InventoryProtocol
{
    private const int MaxUnknownBlockWarns = 64;

    private readonly NetworkSession _session;
    private readonly HashSet<int> _warnedUnknownBlocks = new();
    private readonly InventoryNetIds _netIds = new();
    private uint _openContainerGeneration;

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
            wire[i] = DescribeForWire(InventorySlotReference.Player(i), slots[i]);

        // Keep cursor advertisement warm even though window 0 payload is bag-only.
        _ = DescribeForWire(InventorySlotReference.Cursor, inventory.Cursor);

        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            Slots = wire
        });
    }

    /// <summary>Window 120 — 4-slot armor (helmet/chestplate/leggings/boots), Phase XI.2.</summary>
    public void SendArmorContent(PlayerInventory inventory)
    {
        var wire = new NetworkItemStack[PlayerInventory.ArmorSize];
        for (var i = 0; i < wire.Length; i++)
            wire[i] = DescribeForWire(InventorySlotReference.Armor(i), inventory.GetArmor(i));

        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContainerMap.WindowArmor,
            Slots = wire
        });
    }

    /// <summary>Peer display (MobArmorEquipment fan-out) — no stack net id allocation.</summary>
    public NetworkItemStack DescribeArmor(PlayerInventory inventory, int slot) =>
        ToNetworkStack(inventory.GetArmor(slot), stackNetworkId: 0);

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
            DescribeForWire(InventorySlotReference.Cursor, player.Inventory.Cursor);

        for (var g = 0; g < PlayerCraftUi.GridSize; g++)
        {
            wire[InventoryContainerMap.CraftingGridWireOffset + g] =
                DescribeForWire(InventorySlotReference.CraftGrid(g), player.CraftUi.GetGrid(g));
        }

        // Always reported, same as the personal grid above — harmless (all-empty) when no crafting
        // table is open, and keeps this one "full window-124 shape" method the single source of
        // truth for both grids (ADR §139).
        for (var g = 0; g < PlayerTableCraftUi.GridSize; g++)
        {
            wire[InventoryContainerMap.CraftingTableGridWireOffset + g] =
                DescribeForWire(InventorySlotReference.TableCraftGrid(g), player.TableCraftUi.GetGrid(g));
        }

        wire[InventoryContainerMap.CraftingResultWireSlot] =
            DescribeForWire(InventorySlotReference.CraftResult, player.CraftUi.Result);

        return wire;
    }

    public void SendChestContent(ChestStore chests, in OpenChestView view)
    {
        chests.Ensure(view.PrimaryX, view.PrimaryY, view.PrimaryZ);
        if (view.TryGetPartner(out var px, out var py, out var pz))
            chests.Ensure(px, py, pz);

        var wire = new NetworkItemStack[view.SlotCount];
        for (var i = 0; i < view.SlotCount; i++)
        {
            wire[i] = DescribeForWire(InventorySlotReference.OpenContainer(i), chests.GetOpen(view, i));
        }

        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowChest,
            Slots = wire
        });
    }

    /// <summary>Legacy single-cell content (tests / call sites that only know a cell).</summary>
    public void SendChestContent(ChestStore chests, int x, int y, int z) =>
        SendChestContent(chests, OpenChestView.Single(x, y, z));

    /// <summary>
    /// Reconciles the player-owned inventory plus the currently visible authoritative view after
    /// a rejected wire request. Container-specific replication stays at the protocol boundary;
    /// inbound handlers do not inspect gameplay container implementations.
    /// </summary>
    public void ResyncActiveInventoryView(global::Zenith.Player.Player player)
    {
        SendInventoryContent(player.Inventory);
        SendUiInventoryContent(player);
        if (player.OpenContainer is { Target: OpenContainerSession.TargetKind.Chest, Chest: { } chest })
            SendChestContent(_session.Context.World.Chests, chest);
    }

    /// <summary>
    /// Single path for InventoryContent / UI / chest / ISR OK: refresh advertisement then
    /// build wire DTO. Prefer this over separate Refresh+Get (ADR §54).
    /// Wire fields: <c>ItemNetworkId</c> + optional <c>BlockRuntimeId</c> + <c>StackNetworkId</c> (ISR) —
    /// three distinct ids (ADR §55 glossary); do not confuse with <c>CreativeNetId</c>.
    /// </summary>
    public NetworkItemStack DescribeForWire(in InventorySlotReference reference, InventorySlot slot)
    {
        var netId = _netIds.Refresh(reference, slot);
        return ToNetworkStack(slot, netId);
    }

    /// <summary>Compatibility overload for flat-index characterization tests.</summary>
    public NetworkItemStack DescribeForWire(int flat, InventorySlot slot)
    {
        if (!InventorySlotReference.TryFromLegacyFlat(flat, out var reference))
            throw new ArgumentOutOfRangeException(nameof(flat));
        return DescribeForWire(reference, slot);
    }

    /// <summary>
    /// Soft ISR match vs last <see cref="DescribeForWire"/> advertisement.
    /// <paramref name="clientStackNetId"/> ≤ 0 → accept (air / prediction deferred).
    /// Positive mismatch → false (caller rejects + resync).
    /// </summary>
    public bool MatchesAdvertisedStackNetId(in InventorySlotReference reference, int clientStackNetId)
    {
        if (clientStackNetId <= 0)
            return true;
        return _netIds.Peek(reference) == clientStackNetId;
    }

    /// <summary>Compatibility overload for flat-index characterization tests.</summary>
    public bool MatchesAdvertisedStackNetId(int flat, int clientStackNetId)
    {
        if (!InventorySlotReference.TryFromLegacyFlat(flat, out var reference))
            return false;
        return MatchesAdvertisedStackNetId(reference, clientStackNetId);
    }

    /// <summary>
    /// Starts a new authoritative open-container view. Its protocol stack IDs cannot be reused
    /// from the previous view, even when the same wire slot contains the same item.
    /// </summary>
    public void BeginOpenContainerSession(uint generation)
    {
        if (_openContainerGeneration == generation) return;
        _openContainerGeneration = generation;
        _netIds.ClearOpenContainer();
    }

    /// <summary>Forgets tokens belonging to a closed open-container view.</summary>
    public void EndOpenContainerSession()
    {
        _openContainerGeneration = 0;
        _netIds.ClearOpenContainer();
    }

    /// <summary>Peer display / AddPlayer — no stack net id allocation.</summary>
    public NetworkItemStack DescribeSlot(PlayerInventory inventory, int slot)
    {
        var stack = inventory.Get(slot);
        return ToNetworkStack(stack, stackNetworkId: 0);
    }

    /// <summary>Floor-drop / AddItemActor — domain StackId + count → wire stack (ADR §55).</summary>
    public NetworkItemStack DescribeStack(StackId id, int count)
    {
        if (count <= 0 || id.IsEmpty)
            return NetworkItemStack.Empty;
        return ToNetworkStack(new InventorySlot(id, count), stackNetworkId: 0);
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

    public void SendCraftingTableOpen(int blockX, int blockY, int blockZ)
    {
        _session.SendDataPacket(new ContainerOpenPacket
        {
            WindowId = (byte)InventoryContainerMap.WindowCraftingTable,
            WindowType = ContainerOpenPacket.WindowTypeWorkbench,
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
            var stack = ResolveStack(player, touch.Reference);
            var wire = DescribeForWire(touch.Reference, stack);
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
            if (!InventorySlotReference.TryFromLegacyFlat(flat, out var reference) ||
                !InventoryContainerMap.TryToWire(reference, out var containerId, out var wireSlot))
                continue;
            touches.Add(new WireTouch(reference, containerId, wireSlot));
        }

        SendItemStackResponseOk(requestId, player, touches);
    }

    private InventorySlot ResolveStack(global::Zenith.Player.Player player, in InventorySlotReference reference) =>
        InventorySlotResolver.GetSlot(player, _session.Context.World, reference);

    private NetworkItemStack ToNetworkStack(InventorySlot slot, int stackNetworkId)
    {
        if (slot.IsEmpty)
            return NetworkItemStack.Empty;

        var palette = _session.Context.ItemPalette;
        if (slot.Id.IsItem)
        {
            if (palette.TryGetName(slot.Id.Value, out _))
                return new NetworkItemStack((short)slot.Id.Value, (ushort)slot.Count, BlockRuntimeId: 0, StackNetworkId: stackNetworkId);
            WarnUnknownStackOnce(slot.Id.Value);
            var airId = palette.Require("minecraft:air");
            return new NetworkItemStack(airId, 0, Blocks.Air);
        }

        if (Blocks.TryGetName(slot.Id.Value, out var blockName) && palette.TryGet(blockName, out var blockNetId))
            return new NetworkItemStack(blockNetId, (ushort)slot.Count, slot.Id.Value, StackNetworkId: stackNetworkId);

        WarnUnknownStackOnce(slot.Id.Value);
        var air = palette.Require("minecraft:air");
        return new NetworkItemStack(air, 0, Blocks.Air);
    }

    private void WarnUnknownStackOnce(int runtimeId)
    {
        if (_warnedUnknownBlocks.Count >= MaxUnknownBlockWarns)
            return;
        if (!_warnedUnknownBlocks.Add(runtimeId))
            return;
        _session.Context.Logger.Warning(
            $"Unknown block runtime id {runtimeId} in inventory wire map; sending air item id (cap {MaxUnknownBlockWarns}).");
    }
}

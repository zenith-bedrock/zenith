using Zenith.Gameplay.Ports;
using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// The slot primitives every inventory action is built from: read, write, transfer, swap, drop and destroy a slot addressed by an
/// <see cref="InventorySlotReference"/>, and decide who may reach which slot. It owns no policy about requests; that is
/// <see cref="StackRequestApplier"/>.
/// </summary>
sealed class InventorySlots
{
    private readonly World.World _world;

    public InventorySlots(World.World world) => _world = world;

    public InventorySlot GetSlot(global::Zenith.Player.Player player, in InventorySlotReference reference) =>
        InventorySlotResolver.GetSlot(player, _world, reference);

    public bool TrySetSlot(global::Zenith.Player.Player player, in InventorySlotReference reference, InventorySlot value) =>
        InventorySlotResolver.TrySetSlot(player, _world, reference, value);

    public bool TryTransfer(global::Zenith.Player.Player player, in InventorySlotReference from, in InventorySlotReference to, int count)
    {
        if (from == to) return false;
        if (!IsValidReference(player, from) || !IsValidReference(player, to)) return false;
        if (count <= 0 || !PlayerInventory.IsValidStackCount(count)) return false;

        var src = GetSlot(player, from);
        if (src.IsEmpty || count > src.Count) return false;
        if (!CanPlaceInArmorSlot(player, to, src)) return false;

        var dst = GetSlot(player, to);
        if (!dst.IsEmpty && dst.Id != src.Id) return false;

        var space = ItemStackLimits.For(src.Id) - (dst.IsEmpty ? 0 : dst.Count);
        if (count > space) return false;

        var newDstCount = (dst.IsEmpty ? 0 : dst.Count) + count;
        var newSrcCount = src.Count - count;

        if (!TrySetSlot(player, to, new InventorySlot(src.Id, newDstCount))) return false;
        if (!TrySetSlot(player, from, newSrcCount == 0 ? InventorySlot.Empty : src with { Count = newSrcCount }))
            return false;
        return true;
    }

    public bool TryRemoveForDrop(
        global::Zenith.Player.Player player,
        in InventorySlotReference from,
        int count,
        List<FloorDropFanout.DepositRequest> pendingDrops)
    {
        if (!IsValidReference(player, from)) return false;
        if (count <= 0 || !PlayerInventory.IsValidStackCount(count)) return false;

        var src = GetSlot(player, from);
        if (src.IsEmpty || count > src.Count) return false;

        var left = src.Count - count;
        if (!TrySetSlot(player, from, left == 0 ? InventorySlot.Empty : src with { Count = left }))
            return false;

        pendingDrops.Add(new FloorDropFanout.DepositRequest(src.Id, count));
        return true;
    }

    public bool TryDestroy(
        global::Zenith.Player.Player player,
        in InventorySlotReference from,
        int count)
    {
        if (!IsValidReference(player, from)) return false;
        if (count <= 0 || !PlayerInventory.IsValidStackCount(count)) return false;

        var src = GetSlot(player, from);
        if (src.IsEmpty || count > src.Count) return false;

        var left = src.Count - count;
        return TrySetSlot(player, from, left == 0 ? InventorySlot.Empty : src with { Count = left });
    }

    public bool TrySwap(global::Zenith.Player.Player player, in InventorySlotReference a, in InventorySlotReference b)
    {
        if (a == b) return false;
        if (!IsValidReference(player, a) || !IsValidReference(player, b)) return false;

        var sa = GetSlot(player, a);
        var sb = GetSlot(player, b);
        if (!CanPlaceInArmorSlot(player, a, sb) || !CanPlaceInArmorSlot(player, b, sa)) return false;
        if (!TrySetSlot(player, a, sb)) return false;
        if (!TrySetSlot(player, b, sa)) return false;
        return true;
    }

    /// <summary>
    /// Server-authoritative armor slot-type guard (Phase XI.2): a piece can only land in the armor
    /// slot matching its equipment kind. Every other area accepts any item, unchanged.
    /// </summary>
    private static bool CanPlaceInArmorSlot(global::Zenith.Player.Player player, in InventorySlotReference reference, InventorySlot value)
    {
        if (reference.Area != InventorySlotArea.Armor) return true;
        if (value.IsEmpty) return true;
        return ArmorItems.TryGet(player.ItemPalette, value.Id, out var slot, out _) &&
               slot == reference.Index;
    }

    public bool IsValidReference(global::Zenith.Player.Player player, in InventorySlotReference reference)
    {
        return reference.Area switch
        {
            InventorySlotArea.OpenContainer => player.Windows.OpenContainer is
                { Target: OpenContainerSession.TargetKind.Chest, Chest: { } view } &&
                reference.Index >= 0 && reference.Index < view.SlotCount && IsOpenChestAccessible(player, view),
            InventorySlotArea.CraftGrid => reference.Index >= 0 && reference.Index < PlayerCraftUi.GridSize,
            InventorySlotArea.TableCraftGrid => player.Windows.IsAtCraftingTable &&
                reference.Index >= 0 && reference.Index < PlayerTableCraftUi.GridSize,
            InventorySlotArea.CraftResult => reference.Index == 0,
            InventorySlotArea.Cursor => reference.Index == 0,
            InventorySlotArea.PlayerInventory => PlayerInventory.IsValidInventorySlot(reference.Index),
            InventorySlotArea.Armor => PlayerInventory.IsValidArmorSlot(reference.Index),
            InventorySlotArea.Offhand => reference.Index == 0,
            _ => false
        };
    }

    private bool IsOpenChestAccessible(global::Zenith.Player.Player player, in OpenChestView view)
    {
        if (!Blocks.IsChest(_world.GetBlock(view.PrimaryX, view.PrimaryY, view.PrimaryZ)))
            return false;

        var primaryInReach = BlockEditSystem.IsWithinReach(player, view.PrimaryX, view.PrimaryY, view.PrimaryZ);
        if (!view.TryGetPartner(out var partnerX, out var partnerY, out var partnerZ))
            return primaryInReach;

        return Blocks.IsChest(_world.GetBlock(partnerX, partnerY, partnerZ)) &&
               (primaryInReach || BlockEditSystem.IsWithinReach(player, partnerX, partnerY, partnerZ));
    }

    private static void AddWireTouch(List<WireTouch> touches, in InventorySlotReference reference, WireSlot wire)
    {
        if (wire.ContainerId == 0 && InventoryContainerMap.TryToWire(reference, out var c, out var s))
            AddWireTouch(touches, reference, c, s);
        else
            AddWireTouch(touches, reference, wire.ContainerId, wire.Slot);
    }

    private static void AddWireTouch(List<WireTouch> touches, in InventorySlotReference reference, byte containerId, byte slot)
    {
        foreach (var t in touches)
        {
            if (t.Reference == reference && t.ContainerId == containerId && t.Slot == slot)
                return;
        }

        touches.Add(new WireTouch(reference, containerId, slot));
    }
}

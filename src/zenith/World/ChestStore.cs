using Zenith.Player;

namespace Zenith.World;

/// <summary>
/// Chest inventories, as a view over the <see cref="ContainerEntity"/> each chest cell carries in its
/// <see cref="WorldColumn"/> (ADR §167). The entity owns the slots and the list of players who have it open; this class
/// is what the inventory systems talk to: single or double chest views (<see cref="OpenChestView"/>), rollback snapshots,
/// and "what falls out when it breaks". Per-cell size is always <see cref="SingleSize"/>; an open UI may be 27 or 54.
/// </summary>
sealed class ChestStore
{
    public const int SingleSize = ContainerEntity.SlotCount;
    public const int DoubleSize = SingleSize * 2;

    private readonly World _world;

    public ChestStore(World world) => _world = world;

    private bool TryGet(int x, int y, int z, out ContainerEntity container) =>
        _world.TryGetBlockEntity(x, y, z, out container);

    public int OpenerCount(int x, int y, int z) => TryGet(x, y, z, out var container) ? container.Openers.Count : 0;

    /// <summary>Returns true when this was the first opener (0→1) — animate lid open.</summary>
    public bool TryAddOpener(int x, int y, int z, long playerRuntimeId)
    {
        if (!TryGet(x, y, z, out var container)) return false;
        return container.Openers.Add(playerRuntimeId) && container.Openers.Count == 1;
    }

    /// <summary>Returns true when this was the last opener (1→0) — animate lid close.</summary>
    public bool TryRemoveOpener(int x, int y, int z, long playerRuntimeId)
    {
        if (!TryGet(x, y, z, out var container)) return false;
        return container.Openers.Remove(playerRuntimeId) && container.Openers.Count == 0;
    }

    /// <summary>Clears all openers; returns true if the lid was open.</summary>
    public bool ClearOpeners(int x, int y, int z)
    {
        if (!TryGet(x, y, z, out var container) || container.Openers.Count == 0) return false;
        container.Openers.Clear();
        return true;
    }

    /// <summary>Makes sure the chest cell has its 27 slots; existing contents are untouched.</summary>
    public bool TryEnsure(int x, int y, int z)
    {
        if (TryGet(x, y, z, out _)) return true;
        _world.SetBlockEntity(x, y, z, new ContainerEntity());
        return true;
    }

    public bool TryGetSlots(int x, int y, int z, out InventorySlot[] slots)
    {
        if (TryGet(x, y, z, out var container))
        {
            slots = container.Slots;
            return true;
        }

        slots = [];
        return false;
    }

    public InventorySlot Get(int x, int y, int z, int slot)
    {
        if ((uint)slot >= SingleSize) return InventorySlot.Empty;
        return TryGet(x, y, z, out var container) ? container.Slots[slot] : InventorySlot.Empty;
    }

    public bool TrySet(int x, int y, int z, int slot, InventorySlot value)
    {
        if ((uint)slot >= SingleSize) return false;
        if (!TryGet(x, y, z, out var container)) return false;
        if (!ItemStackLimits.CanReplace(container.Slots[slot], value)) return false;
        container.Slots[slot] = value.IsEmpty ? InventorySlot.Empty : value;
        _world.MarkBlockEntityDirty(x, z);
        return true;
    }

    /// <summary>Open-UI slot: 0..26 primary cell; 27..53 partner when double (ADR §56).</summary>
    public InventorySlot GetOpen(in OpenChestView view, int openSlot)
    {
        if ((uint)openSlot >= (uint)view.SlotCount) return InventorySlot.Empty;
        if (openSlot < SingleSize)
            return Get(view.PrimaryX, view.PrimaryY, view.PrimaryZ, openSlot);
        if (!view.TryGetPartner(out var px, out var py, out var pz))
            return InventorySlot.Empty;
        return Get(px, py, pz, openSlot - SingleSize);
    }

    public bool TrySetOpen(in OpenChestView view, int openSlot, InventorySlot value)
    {
        if ((uint)openSlot >= (uint)view.SlotCount) return false;
        if (openSlot < SingleSize)
            return TrySet(view.PrimaryX, view.PrimaryY, view.PrimaryZ, openSlot, value);
        if (!view.TryGetPartner(out var px, out var py, out var pz))
            return false;
        return TrySet(px, py, pz, openSlot - SingleSize, value);
    }

    /// <summary>Snapshot all open slots (27 or 54) for ISR rollback.</summary>
    public InventorySlot[] CaptureOpenSnapshot(in OpenChestView view)
    {
        var copy = new InventorySlot[view.SlotCount];
        for (var i = 0; i < view.SlotCount; i++)
            copy[i] = GetOpen(view, i);
        return copy;
    }

    /// <summary>Rolls the open view back to a <see cref="CaptureOpenSnapshot"/>; cells are never created here, a cell that no longer exists stays gone.</summary>
    public void RestoreOpenSnapshot(in OpenChestView view, InventorySlot[] snapshot)
    {
        if (snapshot.Length != view.SlotCount) return;
        for (var i = 0; i < view.SlotCount; i++)
        {
            // Rollback restores exact pre-existing custody, including legacy oversized stacks.
            if (i < SingleSize)
            {
                if (TryGetSlots(view.PrimaryX, view.PrimaryY, view.PrimaryZ, out var primary)) primary[i] = snapshot[i];
            }
            else if (view.TryGetPartner(out var x, out var y, out var z) && TryGetSlots(x, y, z, out var partner))
                partner[i - SingleSize] = snapshot[i];
        }

        _world.MarkBlockEntityDirty(view.PrimaryX, view.PrimaryZ);
        if (view.TryGetPartner(out var partnerX, out _, out var partnerZ))
            _world.MarkBlockEntityDirty(partnerX, partnerZ);
    }

    /// <summary>Removes the chest and returns what was inside as (StackId, count) pairs.</summary>
    public List<(StackId Id, int Count)> RemoveAndDump(int x, int y, int z)
    {
        var list = new List<(StackId, int)>();
        if (!_world.RemoveBlockEntity(x, y, z, out var removed) || removed is not ContainerEntity container)
            return list;

        foreach (var slot in container.Slots)
        {
            if (!slot.IsEmpty)
                list.Add((slot.Id, slot.Count));
        }

        return list;
    }

    public InventorySlot[] CaptureSnapshot(int x, int y, int z)
    {
        if (!TryGet(x, y, z, out var container))
            return Array.Empty<InventorySlot>();
        var copy = new InventorySlot[SingleSize];
        Array.Copy(container.Slots, copy, SingleSize);
        return copy;
    }
}

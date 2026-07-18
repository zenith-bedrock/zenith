using Zenith.World;

namespace Zenith.Player;

/// <summary>Domain inventory stack (ADR §55). Identity is <see cref="StackId"/> — never a bare ambiguous runtime id.</summary>
readonly record struct InventorySlot(StackId Id, int Count)
{
    public static InventorySlot Empty => new(StackId.FromBlock(Blocks.Air), 0);

    public static InventorySlot OfBlock(int blockRuntimeId, int count) =>
        new(StackId.FromBlock(blockRuntimeId), count);

    public static InventorySlot OfItem(int itemNetworkId, int count) =>
        new(StackId.FromItem(itemNetworkId), count);

    public bool IsEmpty =>
        Count <= 0 || Id.IsAirBlock || (Id.IsBlock && Id.Value == Blocks.Air);
}

/// <summary>
/// Inventário principal do jogador (36 slots) + cursor de drag.
/// Hotbar 0–8: held / place / MobEquipment. Storage 9–35: storage + TryAdd.
/// Cursor: efêmero (ISR take/place); fora do SnapshotMainInventory.
/// </summary>
sealed class PlayerInventory
{
    public const int HotbarSize = 9;
    public const int MaxStack = 64;
    public const int FullInventorySize = 36;

    /// <summary>Sentinel de domínio para o cursor ISR (não é slot 0–35).</summary>
    public const int CursorSlot = -1;

    private readonly InventorySlot[] _slots = new InventorySlot[FullInventorySize];
    private InventorySlot _cursor = InventorySlot.Empty;

    /// <param name="seedStarterHotbar">
    /// Survival default seed. Creative joins use empty hotbar (client UI supplies items) — ADR §31.
    /// </param>
    public PlayerInventory(bool seedStarterHotbar = true)
    {
        if (seedStarterHotbar)
        {
            _slots[0] = InventorySlot.OfBlock(Blocks.Stone, MaxStack);
            _slots[1] = InventorySlot.OfBlock(Blocks.Dirt, MaxStack);
            _slots[2] = InventorySlot.OfBlock(Blocks.OakPlanks, MaxStack);
            _slots[3] = InventorySlot.OfBlock(Blocks.OakLog, 32);
            _slots[4] = InventorySlot.OfBlock(Blocks.Sand, MaxStack);
            _slots[5] = InventorySlot.OfBlock(Blocks.Chest, 16);
            for (var i = 6; i < FullInventorySize; i++)
                _slots[i] = InventorySlot.Empty;
        }
        else
        {
            for (var i = 0; i < FullInventorySize; i++)
                _slots[i] = InventorySlot.Empty;
        }
    }

    public InventorySlot Cursor => _cursor;

    public static bool IsValidHotbarSlot(int slot) => slot is >= 0 and < HotbarSize;

    public static bool IsValidInventorySlot(int slot) => slot is >= 0 and < FullInventorySize;

    public static bool IsValidLocation(int slot) =>
        slot == CursorSlot || IsValidInventorySlot(slot);

    public static bool IsValidStackCount(int count) => count is >= 0 and <= MaxStack;

    public InventorySlot Get(int slot)
    {
        if (slot == CursorSlot) return _cursor;
        if (!IsValidInventorySlot(slot)) return InventorySlot.Empty;
        return _slots[slot];
    }

    public bool TrySet(int slot, StackId id, int count)
    {
        if (slot == CursorSlot)
        {
            if (!IsValidStackCount(count)) return false;
            _cursor = count == 0 || id.IsAirBlock || (id.IsBlock && id.Value == Blocks.Air)
                ? InventorySlot.Empty
                : new InventorySlot(id, count);
            return true;
        }

        if (!IsValidInventorySlot(slot) || !IsValidStackCount(count)) return false;
        if (count == 0 || id.IsAirBlock || (id.IsBlock && id.Value == Blocks.Air))
        {
            _slots[slot] = InventorySlot.Empty;
            return true;
        }

        _slots[slot] = new InventorySlot(id, count);
        return true;
    }

    /// <summary>Convenience for block stacks (placeable / recipes).</summary>
    public bool TrySetBlock(int slot, int blockRuntimeId, int count) =>
        TrySet(slot, StackId.FromBlock(blockRuntimeId), count);

    public bool TrySetItem(int slot, int itemNetworkId, int count) =>
        TrySet(slot, StackId.FromItem(itemNetworkId), count);

    public StackId GetStackId(int slot)
    {
        var s = Get(slot);
        return s.IsEmpty ? StackId.FromBlock(Blocks.Air) : s.Id;
    }

    /// <summary>Consome 1 do slot de hotbar no tick (place). Só stacks Block placeable.</summary>
    public bool TryConsumeOne(int slot)
    {
        if (!IsValidHotbarSlot(slot)) return false;
        var s = _slots[slot];
        if (s.IsEmpty || !s.Id.IsBlock) return false;
        if (s.Count == 1)
            _slots[slot] = InventorySlot.Empty;
        else
            _slots[slot] = s with { Count = s.Count - 1 };
        return true;
    }

    /// <summary>Remove count of exact <paramref name="id"/> in window 0–35 (all-or-nothing).</summary>
    public bool TryConsume(StackId id, int count)
    {
        if (count <= 0 || id.IsAirBlock) return false;

        var available = 0;
        for (var i = 0; i < FullInventorySize; i++)
        {
            var s = _slots[i];
            if (!s.IsEmpty && s.Id == id)
                available += s.Count;
        }

        if (available < count) return false;

        var remaining = count;
        for (var i = 0; i < FullInventorySize && remaining > 0; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || s.Id != id) continue;
            var take = Math.Min(s.Count, remaining);
            var left = s.Count - take;
            _slots[i] = left == 0 ? InventorySlot.Empty : s with { Count = left };
            remaining -= take;
        }

        return true;
    }

    /// <summary>Block-only consume (recipes). Exact block runtime id match.</summary>
    public bool TryConsumeBlock(int blockRuntimeId, int count) =>
        TryConsume(StackId.FromBlock(blockRuntimeId), count);

    public bool TryAdd(StackId id, int count = 1)
    {
        if (count <= 0 || id.IsAirBlock) return false;

        var snap = CaptureSnapshot();
        var added = TryAddUpTo(id, count);
        if (added != count)
        {
            RestoreSnapshot(snap);
            return false;
        }

        return true;
    }

    public bool TryAddBlock(int blockRuntimeId, int count = 1) =>
        TryAdd(StackId.FromBlock(blockRuntimeId), count);

    public int TryAddUpTo(StackId id, int count)
    {
        if (count <= 0 || id.IsAirBlock) return 0;

        if (id.IsItem)
            return TryAddUpToExact(id, count);

        var mergeRid = Blocks.NormalizeMergeRuntimeId(id.Value);
        var mergeId = StackId.FromBlock(mergeRid);
        var remaining = count;
        for (var i = 0; i < FullInventorySize && remaining > 0; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || !s.Id.IsBlock || !Blocks.SameMergeItem(mergeRid, s.Id.Value))
                continue;
            var space = MaxStack - s.Count;
            if (space <= 0) continue;
            var add = Math.Min(space, remaining);
            _slots[i] = new InventorySlot(s.Id, s.Count + add);
            remaining -= add;
        }

        for (var i = 0; i < FullInventorySize && remaining > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            var add = Math.Min(MaxStack, remaining);
            _slots[i] = new InventorySlot(mergeId, add);
            remaining -= add;
        }

        return count - remaining;
    }

    public int TryAddUpToBlock(int blockRuntimeId, int count) =>
        TryAddUpTo(StackId.FromBlock(blockRuntimeId), count);

    private int TryAddUpToExact(StackId id, int count)
    {
        var remaining = count;
        for (var i = 0; i < FullInventorySize && remaining > 0; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || s.Id != id) continue;
            var space = MaxStack - s.Count;
            if (space <= 0) continue;
            var add = Math.Min(space, remaining);
            _slots[i] = new InventorySlot(s.Id, s.Count + add);
            remaining -= add;
        }

        for (var i = 0; i < FullInventorySize && remaining > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            var add = Math.Min(MaxStack, remaining);
            _slots[i] = new InventorySlot(id, add);
            remaining -= add;
        }

        return count - remaining;
    }

    public bool TryTransfer(int from, int to, int count)
    {
        if (from == to) return false;
        if (!IsValidLocation(from) || !IsValidLocation(to)) return false;
        if (count <= 0 || !IsValidStackCount(count)) return false;

        var src = Get(from);
        if (src.IsEmpty || count > src.Count) return false;

        var dst = Get(to);
        if (!dst.IsEmpty && dst.Id != src.Id) return false;

        var space = dst.IsEmpty ? MaxStack : MaxStack - dst.Count;
        if (count > space) return false;

        var newDstCount = (dst.IsEmpty ? 0 : dst.Count) + count;
        var newSrcCount = src.Count - count;

        SetLocation(to, new InventorySlot(src.Id, newDstCount));
        SetLocation(from, newSrcCount == 0 ? InventorySlot.Empty : src with { Count = newSrcCount });
        return true;
    }

    public bool TrySwap(int a, int b)
    {
        if (a == b) return false;
        if (!IsValidLocation(a) || !IsValidLocation(b)) return false;

        var sa = Get(a);
        var sb = Get(b);
        SetLocation(a, sb);
        SetLocation(b, sa);
        return true;
    }

    public InventorySnapshot CaptureSnapshot()
    {
        var slots = new InventorySlot[FullInventorySize];
        Array.Copy(_slots, slots, FullInventorySize);
        return new InventorySnapshot(slots, _cursor);
    }

    public void RestoreSnapshot(in InventorySnapshot snapshot)
    {
        Array.Copy(snapshot.Slots, _slots, FullInventorySize);
        _cursor = snapshot.Cursor;
    }

    public InventorySlot[] SnapshotMainInventory()
    {
        var all = new InventorySlot[FullInventorySize];
        Array.Copy(_slots, all, FullInventorySize);
        return all;
    }

    public bool TryLoadMainFromBlob(ReadOnlySpan<byte> data)
    {
        Span<InventorySlot> slots = stackalloc InventorySlot[FullInventorySize];
        if (!SlotBlob.TryUnpack(data, slots))
            return false;
        slots.CopyTo(_slots);
        _cursor = InventorySlot.Empty;
        return true;
    }

    public byte[] PackMainBlob() => SlotBlob.Pack(_slots);

    private void SetLocation(int slot, InventorySlot value)
    {
        if (slot == CursorSlot)
            _cursor = value;
        else
            _slots[slot] = value;
    }
}

readonly struct InventorySnapshot(InventorySlot[] slots, InventorySlot cursor)
{
    public InventorySlot[] Slots { get; } = slots;
    public InventorySlot Cursor { get; } = cursor;
}

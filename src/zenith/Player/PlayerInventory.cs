using Zenith.Player;
using Zenith.World;

namespace Zenith.Player;

readonly record struct InventorySlot(int RuntimeId, int Count)
{
    public static InventorySlot Empty => new(Blocks.Air, 0);

    public bool IsEmpty => Count <= 0 || RuntimeId == Blocks.Air;
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
            _slots[0] = new InventorySlot(Blocks.Stone, MaxStack);
            _slots[1] = new InventorySlot(Blocks.Dirt, MaxStack);
            _slots[2] = new InventorySlot(Blocks.OakPlanks, MaxStack);
            _slots[3] = new InventorySlot(Blocks.OakLog, 32);
            _slots[4] = new InventorySlot(Blocks.Sand, MaxStack);
            _slots[5] = new InventorySlot(Blocks.Chest, 16);
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

    public bool TrySet(int slot, int runtimeId, int count)
    {
        if (slot == CursorSlot)
        {
            if (!IsValidStackCount(count)) return false;
            _cursor = count == 0 || runtimeId == Blocks.Air
                ? InventorySlot.Empty
                : new InventorySlot(runtimeId, count);
            return true;
        }

        if (!IsValidInventorySlot(slot) || !IsValidStackCount(count)) return false;
        if (count == 0 || runtimeId == Blocks.Air)
        {
            _slots[slot] = InventorySlot.Empty;
            return true;
        }

        _slots[slot] = new InventorySlot(runtimeId, count);
        return true;
    }

    public int GetRuntimeId(int slot)
    {
        var s = Get(slot);
        return s.Count > 0 ? s.RuntimeId : Blocks.Air;
    }

    /// <summary>Consome 1 do slot de hotbar no tick (place). Slots 9–35 / cursor não são placeáveis.</summary>
    public bool TryConsumeOne(int slot)
    {
        if (!IsValidHotbarSlot(slot)) return false;
        var s = _slots[slot];
        if (s.IsEmpty) return false;
        if (s.Count == 1)
            _slots[slot] = InventorySlot.Empty;
        else
            _slots[slot] = s with { Count = s.Count - 1 };
        return true;
    }

    /// <summary>Remove <paramref name="count"/> de <paramref name="runtimeId"/> na janela 0–35 (all-or-nothing parcial falha).</summary>
    public bool TryConsume(int runtimeId, int count)
    {
        if (count <= 0 || runtimeId == Blocks.Air) return false;

        var available = 0;
        for (var i = 0; i < FullInventorySize; i++)
        {
            var s = _slots[i];
            if (!s.IsEmpty && s.RuntimeId == runtimeId)
                available += s.Count;
        }

        if (available < count) return false;

        var remaining = count;
        for (var i = 0; i < FullInventorySize && remaining > 0; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || s.RuntimeId != runtimeId) continue;
            var take = Math.Min(s.Count, remaining);
            var left = s.Count - take;
            _slots[i] = left == 0 ? InventorySlot.Empty : s with { Count = left };
            remaining -= take;
        }

        return true;
    }

    /// <summary>
    /// Adiciona à janela 0–35: empilha em stacks existentes, depois primeiro vazio.
    /// All-or-nothing: se não couber o pedido inteiro, restaura snapshot (ADR §26 adendo).
    /// Não toca no cursor.
    /// </summary>
    public bool TryAdd(int blockRuntimeId, int count = 1)
    {
        if (count <= 0 || blockRuntimeId == Blocks.Air) return false;

        var snap = CaptureSnapshot();
        var added = TryAddUpTo(blockRuntimeId, count);
        if (added != count)
        {
            RestoreSnapshot(snap);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Empilha o máximo possível (stacks existentes + vazios). Retorna quantos foram adicionados (0..count).
    /// </summary>
    public int TryAddUpTo(int blockRuntimeId, int count)
    {
        if (count <= 0 || blockRuntimeId == Blocks.Air) return 0;

        var mergeRid = Blocks.NormalizeMergeRuntimeId(blockRuntimeId);
        var remaining = count;
        for (var i = 0; i < FullInventorySize && remaining > 0; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || !Blocks.SameMergeItem(mergeRid, s.RuntimeId)) continue;
            var space = MaxStack - s.Count;
            if (space <= 0) continue;
            var add = Math.Min(space, remaining);
            _slots[i] = new InventorySlot(s.RuntimeId, s.Count + add);
            remaining -= add;
        }

        for (var i = 0; i < FullInventorySize && remaining > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            var add = Math.Min(MaxStack, remaining);
            _slots[i] = new InventorySlot(mergeRid, add);
            remaining -= add;
        }

        return count - remaining;
    }

    /// <summary>
    /// Move <paramref name="count"/> de <paramref name="from"/> → <paramref name="to"/> (flat ou cursor).
    /// Dest Occupied com runtime diferente: falha (usar <see cref="TrySwap"/>).
    /// </summary>
    public bool TryTransfer(int from, int to, int count)
    {
        if (from == to) return false;
        if (!IsValidLocation(from) || !IsValidLocation(to)) return false;
        if (count <= 0 || !IsValidStackCount(count)) return false;

        var src = Get(from);
        if (src.IsEmpty || count > src.Count) return false;

        var dst = Get(to);
        if (!dst.IsEmpty && dst.RuntimeId != src.RuntimeId) return false;

        var space = dst.IsEmpty ? MaxStack : MaxStack - dst.Count;
        if (count > space) return false;

        var newDstCount = (dst.IsEmpty ? 0 : dst.Count) + count;
        var newSrcCount = src.Count - count;

        SetLocation(to, new InventorySlot(src.RuntimeId, newDstCount));
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

    /// <summary>Snapshot para rollback all-or-nothing de uma ISR request.</summary>
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

    /// <summary>Cópia dos 36 slots para sync InventoryContent (window 0). Sem cursor.</summary>
    public InventorySlot[] SnapshotMainInventory()
    {
        var all = new InventorySlot[FullInventorySize];
        Array.Copy(_slots, all, FullInventorySize);
        return all;
    }

    /// <summary>Restore 36 main slots from packed blob; clears cursor (ADR §39).</summary>
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

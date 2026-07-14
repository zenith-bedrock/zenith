using Zenith.World;

namespace Zenith.Player;

readonly record struct InventorySlot(int RuntimeId, int Count)
{
    public static InventorySlot Empty => new(Blocks.Air, 0);

    public bool IsEmpty => Count <= 0 || RuntimeId == Blocks.Air;
}

/// <summary>Hotbar 0–8. Bounds de índice/count são responsabilidade do handler antes da intent.</summary>
sealed class PlayerInventory
{
    public const int HotbarSize = 9;
    public const int MaxStack = 64;
    public const int FullInventorySize = 36;

    private readonly InventorySlot[] _hotbar = new InventorySlot[HotbarSize];

    public PlayerInventory()
    {
        _hotbar[0] = new InventorySlot(Blocks.Stone, MaxStack);
        for (var i = 1; i < HotbarSize; i++)
            _hotbar[i] = InventorySlot.Empty;
    }

    public static bool IsValidHotbarSlot(int slot) => slot is >= 0 and < HotbarSize;

    public static bool IsValidStackCount(int count) => count is >= 0 and <= MaxStack;

    public InventorySlot Get(int slot)
    {
        if (!IsValidHotbarSlot(slot)) return InventorySlot.Empty;
        return _hotbar[slot];
    }

    public bool TrySet(int slot, int runtimeId, int count)
    {
        if (!IsValidHotbarSlot(slot) || !IsValidStackCount(count)) return false;
        if (count == 0 || runtimeId == Blocks.Air)
        {
            _hotbar[slot] = InventorySlot.Empty;
            return true;
        }

        _hotbar[slot] = new InventorySlot(runtimeId, count);
        return true;
    }

    public int GetRuntimeId(int slot)
    {
        var s = Get(slot);
        return s.Count > 0 ? s.RuntimeId : Blocks.Air;
    }

    /// <summary>Consome 1 do slot no tick (após bounds no handler).</summary>
    public bool TryConsumeOne(int slot)
    {
        if (!IsValidHotbarSlot(slot)) return false;
        var s = _hotbar[slot];
        if (s.IsEmpty) return false;
        if (s.Count == 1)
            _hotbar[slot] = InventorySlot.Empty;
        else
            _hotbar[slot] = s with { Count = s.Count - 1 };
        return true;
    }

    /// <summary>
    /// Adiciona <paramref name="count"/> do bloco ao hotbar: primeiro empilha no mesmo
    /// runtime id, senão usa o primeiro slot vazio. Retorna false se não couber.
    /// </summary>
    public bool TryAdd(int blockRuntimeId, int count = 1)
    {
        if (count <= 0 || blockRuntimeId == Blocks.Air) return false;

        var remaining = count;
        for (var i = 0; i < HotbarSize && remaining > 0; i++)
        {
            var s = _hotbar[i];
            if (s.IsEmpty || s.RuntimeId != blockRuntimeId) continue;
            var space = MaxStack - s.Count;
            if (space <= 0) continue;
            var add = Math.Min(space, remaining);
            _hotbar[i] = new InventorySlot(blockRuntimeId, s.Count + add);
            remaining -= add;
        }

        for (var i = 0; i < HotbarSize && remaining > 0; i++)
        {
            if (!_hotbar[i].IsEmpty) continue;
            var add = Math.Min(MaxStack, remaining);
            _hotbar[i] = new InventorySlot(blockRuntimeId, add);
            remaining -= add;
        }

        return remaining == 0;
    }

    /// <summary>Slots 0–8 do inventário principal; 9–35 vazios (layout client 36).</summary>
    public InventorySlot[] SnapshotMainInventory()
    {
        var all = new InventorySlot[FullInventorySize];
        for (var i = 0; i < HotbarSize; i++)
            all[i] = _hotbar[i];
        for (var i = HotbarSize; i < FullInventorySize; i++)
            all[i] = InventorySlot.Empty;
        return all;
    }
}

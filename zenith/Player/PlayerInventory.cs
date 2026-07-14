using Zenith.World;

namespace Zenith.Player;

readonly record struct InventorySlot(int RuntimeId, int Count)
{
    public static InventorySlot Empty => new(Blocks.Air, 0);
}

/// <summary>Hotbar 0–8. Bounds de índice/count são responsabilidade do handler antes da intent.</summary>
sealed class PlayerInventory
{
    public const int HotbarSize = 9;
    public const int MaxStack = 64;

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
        if (s.Count <= 0 || s.RuntimeId == Blocks.Air) return false;
        if (s.Count == 1)
            _hotbar[slot] = InventorySlot.Empty;
        else
            _hotbar[slot] = s with { Count = s.Count - 1 };
        return true;
    }
}

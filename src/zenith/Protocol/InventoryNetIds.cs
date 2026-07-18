using Zenith.Player;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>
/// Per-session stack network ids for ISR (wire map SSOT:
/// <see cref="InventoryContainerMap"/>). Callers must not Refresh+Get ad hoc —
/// use <see cref="InventoryProtocol.DescribeForWire"/> / <see cref="InventoryProtocol.MatchesAdvertisedStackNetId"/>.
/// </summary>
sealed class InventoryNetIds
{
    private readonly int[] _slotNetIds = new int[PlayerInventory.FullInventorySize];
    private readonly int[] _chestNetIds = new int[ChestStore.Size];
    private readonly int[] _craftNetIds = new int[PlayerCraftUi.GridSize + 1];
    private readonly Dictionary<int, (StackId Id, int Count)> _stackIdentity = new();
    private int _cursorNetId;
    private int _nextNetId = 1;

    public int Allocate() => _nextNetId++;

    /// <summary>Last advertised id for <paramref name="flat"/> (no remint).</summary>
    public int Peek(int flat)
    {
        if (flat == PlayerInventory.CursorSlot) return _cursorNetId;
        if (InventoryContainerMap.IsChestFlat(flat))
            return _chestNetIds[flat - InventoryContainerMap.ChestBase];
        if (InventoryContainerMap.IsCraftUiFlat(flat))
            return _craftNetIds[flat - InventoryContainerMap.CraftUiBase];
        return _slotNetIds[flat];
    }

    public void Set(int flat, int netId)
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

    /// <summary>
    /// Remint when empty→air or (runtimeId, count) changes — Protocol-side identity until
    /// domain stacks own ids (DF-style). Deferred: NBT/damage identity.
    /// </summary>
    public int Refresh(int flat, InventorySlot slot)
    {
        if (slot.IsEmpty)
        {
            Set(flat, 0);
            _stackIdentity.Remove(flat);
            return 0;
        }

        if (_stackIdentity.TryGetValue(flat, out var prev) &&
            prev.Id == slot.Id && prev.Count == slot.Count)
            return Peek(flat);

        var id = Allocate();
        Set(flat, id);
        _stackIdentity[flat] = (slot.Id, slot.Count);
        return id;
    }
}

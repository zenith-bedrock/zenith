namespace Zenith.Player;

/// <summary>Ephemeral 2×2 craft grid + created output — not persisted (ADR §39).</summary>
sealed class PlayerCraftUi
{
    public const int GridSize = 4;

    private readonly InventorySlot[] _grid = new InventorySlot[GridSize];
    private InventorySlot _result = InventorySlot.Empty;

    public InventorySlot GetGrid(int index) =>
        index is >= 0 and < GridSize ? _grid[index] : InventorySlot.Empty;

    public InventorySlot Result => _result;

    public bool TrySetGrid(int index, InventorySlot slot)
    {
        if (index is < 0 or >= GridSize) return false;
        _grid[index] = slot;
        return true;
    }

    public bool TrySetResult(InventorySlot slot)
    {
        _result = slot;
        return true;
    }

    public InventorySlot[] CaptureSnapshot()
    {
        var snap = new InventorySlot[GridSize + 1];
        Array.Copy(_grid, snap, GridSize);
        snap[GridSize] = _result;
        return snap;
    }

    public void RestoreSnapshot(InventorySlot[] snap)
    {
        if (snap.Length != GridSize + 1) return;
        Array.Copy(snap, _grid, GridSize);
        _result = snap[GridSize];
    }

    public void Clear()
    {
        for (var i = 0; i < GridSize; i++)
            _grid[i] = InventorySlot.Empty;
        _result = InventorySlot.Empty;
    }
}

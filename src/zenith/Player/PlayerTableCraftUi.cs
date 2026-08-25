namespace Zenith.Player;

/// <summary>
/// Ephemeral 3×3 crafting-table grid (ADR §139) — the real Bedrock "workbench" UI, wire slots
/// 32-40 (<see cref="Protocol.InventoryContainerMap.CraftingTableGridWireOffset"/>), distinct from
/// <see cref="PlayerCraftUi"/>'s personal 2×2 grid (wire slots 28-31) rather than an extension of
/// it — the two occupy separate, non-overlapping wire slot ranges on real Bedrock clients, confirmed
/// against Dragonfly/pmmp's protocol constants. Not persisted, same lifecycle as
/// <see cref="PlayerCraftUi"/>; no created-output slot of its own — see <see cref="ICraftGrid"/>'s
/// doc comment for why.
/// </summary>
sealed class PlayerTableCraftUi : ICraftGrid
{
    public const int GridSize = 9;
    int ICraftGrid.GridSize => GridSize;

    private readonly InventorySlot[] _grid = new InventorySlot[GridSize];

    public InventorySlot GetGrid(int index) =>
        index is >= 0 and < GridSize ? _grid[index] : InventorySlot.Empty;

    public bool TrySetGrid(int index, InventorySlot slot)
    {
        if (index is < 0 or >= GridSize) return false;
        _grid[index] = slot;
        return true;
    }

    public InventorySlot[] CaptureSnapshot()
    {
        var snap = new InventorySlot[GridSize];
        Array.Copy(_grid, snap, GridSize);
        return snap;
    }

    public void RestoreSnapshot(InventorySlot[] snapshot)
    {
        if (snapshot.Length != GridSize) return;
        Array.Copy(snapshot, _grid, GridSize);
    }

    public void Clear()
    {
        for (var i = 0; i < GridSize; i++)
            _grid[i] = InventorySlot.Empty;
    }
}

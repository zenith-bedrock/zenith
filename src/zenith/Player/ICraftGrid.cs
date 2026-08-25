namespace Zenith.Player;

/// <summary>
/// The grid-only shape <see cref="Gameplay.Inventory.RecipeRegistry.TryCraftFromGrid"/> actually
/// needs (ADR §139) — read/write cells and snapshot/restore for rollback. Deliberately excludes the
/// created-output slot: the wire's CreatedOutput slot (50) is shared/unchanged between the personal
/// 2×2 grid and a crafting table's 3×3 grid, so there is exactly one result holder
/// (<see cref="PlayerCraftUi.Result"/>) regardless of which grid produced it — duplicating that
/// concept onto <see cref="PlayerTableCraftUi"/> would model state that doesn't exist on the wire.
/// <see cref="PlayerCraftUi"/> (4 cells) and <see cref="PlayerTableCraftUi"/> (9 cells) are the two
/// real implementations; nothing else is planned.
/// </summary>
interface ICraftGrid
{
    int GridSize { get; }
    InventorySlot GetGrid(int index);
    bool TrySetGrid(int index, InventorySlot slot);
    InventorySlot[] CaptureSnapshot();
    void RestoreSnapshot(InventorySlot[] snapshot);
}

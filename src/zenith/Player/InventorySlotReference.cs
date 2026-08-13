namespace Zenith.Player;

/// <summary>
/// A location in the inventory state owned by one player/container session. This is deliberately
/// a domain reference: it has no Bedrock container or window identifier. Resolving an
/// <see cref="OpenContainer"/> reference consults the player's authoritative open-container
/// session at apply time.
/// </summary>
readonly record struct InventorySlotReference(InventorySlotArea Area, int Index)
{
    public static InventorySlotReference Player(int index) => new(InventorySlotArea.PlayerInventory, index);
    public static InventorySlotReference Cursor => new(InventorySlotArea.Cursor, 0);
    public static InventorySlotReference OpenContainer(int index) => new(InventorySlotArea.OpenContainer, index);
    public static InventorySlotReference CraftGrid(int index) => new(InventorySlotArea.CraftGrid, index);
    public static InventorySlotReference CraftResult => new(InventorySlotArea.CraftResult, 0);
    public static InventorySlotReference Armor(int index) => new(InventorySlotArea.Armor, index);

    /// <summary>
    /// Compatibility only for existing in-process characterization tests. Production packet
    /// decoding must use <c>InventoryContainerMap.TryMap</c>, which creates this reference
    /// directly instead of leaking a protocol-derived flat number into gameplay.
    /// </summary>
    public static bool TryFromLegacyFlat(int flat, out InventorySlotReference reference)
    {
        if (flat == PlayerInventory.CursorSlot)
        {
            reference = Cursor;
            return true;
        }

        if (flat is >= 100 and < 100 + World.ChestStore.DoubleSize)
        {
            reference = OpenContainer(flat - 100);
            return true;
        }

        if (flat is >= 200 and < 200 + PlayerCraftUi.GridSize)
        {
            reference = CraftGrid(flat - 200);
            return true;
        }

        if (flat == 200 + PlayerCraftUi.GridSize)
        {
            reference = CraftResult;
            return true;
        }

        if (PlayerInventory.IsValidInventorySlot(flat))
        {
            reference = Player(flat);
            return true;
        }

        reference = default;
        return false;
    }
}

/// <summary>Inventory storage area, independent of any wire container ID.</summary>
public enum InventorySlotArea : byte
{
    PlayerInventory = 1,
    Cursor = 2,
    OpenContainer = 3,
    CraftGrid = 4,
    CraftResult = 5,
    Armor = 6
}

using Zenith.Gameplay.Inventory;
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
    public static InventorySlotReference Offhand => new(InventorySlotArea.Offhand, 0);
    public static InventorySlotReference Armor(int index) => new(InventorySlotArea.Armor, index);
    public static InventorySlotReference TableCraftGrid(int index) => new(InventorySlotArea.TableCraftGrid, index);
}

/// <summary>Inventory storage area, independent of any wire container ID.</summary>
public enum InventorySlotArea : byte
{
    PlayerInventory = 1,
    Cursor = 2,
    OpenContainer = 3,
    CraftGrid = 4,
    CraftResult = 5,
    Armor = 6,
    /// <summary>Crafting table's 3×3 grid (ADR §139) — separate wire slot range from <see cref="CraftGrid"/>, not an extension of it.</summary>
    TableCraftGrid = 7,
    /// <summary>The single offhand slot: a player-owned slot like armor, not a UI one.</summary>
    Offhand = 8
}

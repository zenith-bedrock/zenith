namespace Zenith.Player;

/// <summary>
/// Inventory / chest window open-close — handler queues; applied on tick before ISR (§54).
/// Same-session Protocol runs after decide.
/// </summary>
readonly struct InventoryWindowIntent
{
    public enum Kind : byte
    {
        OpenInventory = 1,
        OpenChest = 2,
        Close = 3
    }

    public bool HasValue { get; init; }
    public Kind Action { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public byte WindowId { get; init; }
    public byte WindowType { get; init; }

    public static InventoryWindowIntent OpenInventory() => new()
    {
        HasValue = true,
        Action = Kind.OpenInventory
    };

    public static InventoryWindowIntent OpenChest(int x, int y, int z) => new()
    {
        HasValue = true,
        Action = Kind.OpenChest,
        X = x,
        Y = y,
        Z = z
    };

    public static InventoryWindowIntent Close(byte windowId, byte windowType) => new()
    {
        HasValue = true,
        Action = Kind.Close,
        WindowId = windowId,
        WindowType = windowType
    };
}

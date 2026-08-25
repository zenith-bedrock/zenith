using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Authoritative per-player view of the one Bedrock container currently open for that connection.
/// The window identity is opaque protocol data; the target is a small domain choice for the
/// containers Zenith ships today. It owns lifecycle only, never inventory transaction rules.
/// </summary>
readonly record struct OpenContainerSession(
    byte WindowId,
    byte WindowType,
    uint Generation,
    OpenContainerSession.TargetKind Target,
    OpenChestView? Chest)
{
    public enum TargetKind : byte
    {
        PlayerInventory = 1,
        Chest = 2,
        CraftingTable = 3
    }

    public static OpenContainerSession PlayerInventory(byte windowId, byte windowType, uint generation) =>
        new(windowId, windowType, generation, TargetKind.PlayerInventory, null);

    public static OpenContainerSession ChestView(
        byte windowId, byte windowType, uint generation, in OpenChestView chest) =>
        new(windowId, windowType, generation, TargetKind.Chest, chest);

    /// <summary>
    /// ADR §139 — a crafting table has no shared/persistent storage (unlike Chest): the 3×3 grid
    /// lives entirely on the opening player's own <see cref="PlayerTableCraftUi"/>, so this session
    /// carries no view payload, just the target kind.
    /// </summary>
    public static OpenContainerSession CraftingTable(byte windowId, byte windowType, uint generation) =>
        new(windowId, windowType, generation, TargetKind.CraftingTable, null);
}

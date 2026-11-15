namespace Zenith.Player;

/// <summary>One stack-bound food use owned by the hunger tick.</summary>
readonly record struct FoodUseState(int Slot, InventorySlot Stack, ulong StartedTick);

readonly record struct FoodUseIntent(int Slot, InventorySlot? ExpectedStack, bool Cancel);

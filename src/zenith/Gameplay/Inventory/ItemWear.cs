using Zenith.Player;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Wear of durable items (ADR §148): pure rules over a stack and the durability table, no player, no packets.
/// The caller decides WHEN something wears and how much; this decides what the stack becomes.
/// </summary>
static class ItemWear
{
    internal enum Outcome : byte
    {
        /// <summary>The item does not wear (not in the durability table); the stack is unchanged.</summary>
        NotDurable,

        /// <summary>The stack took the damage and survives.</summary>
        Worn,

        /// <summary>The damage reached the item's maximum: the item is gone (the stack loses one item).</summary>
        Broke,
    }

    /// <summary>
    /// Applies <paramref name="amount"/> points of wear to one item of <paramref name="stack"/>. A durable item breaks
    /// when its damage reaches its maximum (BDS: <c>damage &gt;= maxDamage</c> destroys the item).
    /// </summary>
    public static Outcome Apply(ItemDurability table, InventorySlot stack, int amount, out InventorySlot result)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        result = stack;
        if (stack.IsEmpty || !table.TryGetMax(stack.Id, out var max))
            return Outcome.NotDurable;

        var damage = stack.Id.Damage + amount;
        if (damage >= max)
        {
            // Durable items do not stack, but stay correct if one ever does: only the worn item is lost.
            result = stack.Count <= 1 ? InventorySlot.Empty : stack with { Count = stack.Count - 1 };
            return Outcome.Broke;
        }

        result = stack with { Id = stack.Id.WithDamage(damage) };
        return Outcome.Worn;
    }
}

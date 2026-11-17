using Zenith.Player;
using Zenith.Gameplay.Inventory;
using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// What fighting does to the gear (plan 004 item 11): a weapon that lands a hit loses durability (1 for a sword or trident, 2 for
/// a tool used as one), and every armor piece loses max(1, damage / 4) when its wearer is hurt by an attack, fire or a blast.
/// Creative gear never wears. The pure rules live in <see cref="ItemWear"/>; this decides how much and writes it back.
/// </summary>
static class GearWear
{
    /// <summary>Wear a held item takes for one landed hit; 0 when it is not a weapon or tool.</summary>
    public static int HitWearOf(string itemName)
    {
        if (itemName == ItemNames.Trident) return 1;
        var bare = ItemNames.Bare(itemName);
        var separator = bare.LastIndexOf('_');
        if (separator < 0) return 0;
        return bare[(separator + 1)..] switch
        {
            "sword" => 1,
            "axe" or "pickaxe" or "shovel" or "hoe" => 2,
            _ => 0
        };
    }

    public static bool WearsArmorFor(DamageCause cause) =>
        cause is DamageCause.Melee or DamageCause.Projectile or DamageCause.Explosion or DamageCause.Fire or DamageCause.Lava;

    public static void WearHeldWeapon(Player.Player attacker)
    {
        if (attacker.GameMode == GameMode.Creative) return;
        var slot = attacker.SelectedHotbarSlot;
        var held = attacker.Inventory.Get(slot);
        if (held.IsEmpty || !held.Id.IsItem || !attacker.ItemPalette.TryGetName(held.Id.Value, out var name)) return;
        var wear = HitWearOf(name);
        if (wear == 0) return;

        var outcome = ItemWear.Apply(attacker.Durability, held, wear, out var after);
        if (outcome == ItemWear.Outcome.NotDurable) return;
        if (!attacker.Inventory.TrySet(slot, after.IsEmpty ? StackId.FromBlock(Blocks.Air) : after.Id, after.IsEmpty ? 0 : after.Count)) return;
        attacker.Client.Inventory.SendInventoryContent(attacker.Inventory);
    }

    public static void WearArmor(Player.Player wearer, float damage)
    {
        if (wearer.GameMode == GameMode.Creative) return;
        var wear = Math.Max(1, (int)MathF.Floor(damage / 4f));
        var changed = false;
        for (var slot = 0; slot < PlayerInventory.ArmorSize; slot++)
        {
            var piece = wearer.Inventory.GetArmor(slot);
            if (piece.IsEmpty) continue;
            if (ItemWear.Apply(wearer.Durability, piece, wear, out var after) == ItemWear.Outcome.NotDurable) continue;
            changed |= wearer.Inventory.TrySetArmor(slot, after.IsEmpty ? StackId.FromBlock(Blocks.Air) : after.Id, after.IsEmpty ? 0 : after.Count);
        }

        if (changed) wearer.Client.Inventory.SendArmorContent(wearer.Inventory);
    }
}

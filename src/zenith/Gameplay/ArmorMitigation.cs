using Zenith.Player;

namespace Zenith.Gameplay;

/// <summary>
/// The one mitigation step between a decided <see cref="DamageSource"/> and <see cref="HealthState"/>
/// (Phase XI.2). Vanilla-parity linear formula (4% per protection point, capped at 20 points / 80%);
/// not a generic damage/stats pipeline — <see cref="PlayerDamage"/> remains the single call site.
/// </summary>
static class ArmorMitigation
{
    private const float PointReduction = 0.04f;
    private const float MaxProtectionPoints = 20f;

    /// <summary>Void, starvation and magic (e.g. Poison) bypass armor (vanilla parity); every other cause is reduced.</summary>
    public static float Apply(Player.Player player, DamageSource source, float amount)
    {
        if (source.Cause is DamageCause.Void or DamageCause.Starve or DamageCause.Magic) return amount;

        var points = TotalProtectionPoints(player);
        if (points <= 0f) return amount;

        var reduction = MathF.Min(MaxProtectionPoints, points) * PointReduction;
        return amount * (1f - reduction);
    }

    public static float TotalProtectionPoints(Player.Player player)
    {
        var palette = player.Session.Context.ItemPalette;
        var total = 0f;
        for (var slot = 0; slot < PlayerInventory.ArmorSize; slot++)
        {
            var stack = player.Inventory.GetArmor(slot);
            if (stack.IsEmpty) continue;
            if (ArmorItems.TryGet(palette, stack.Id, out _, out var protection))
                total += protection;
        }

        return total;
    }
}

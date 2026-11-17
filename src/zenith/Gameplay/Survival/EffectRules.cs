namespace Zenith.Gameplay.Survival;

/// <summary>
/// What each effect changes in the rules of the game (plan 004 item 26). The effects the client simulates itself (speed,
/// slowness, jump boost, nausea, blindness, night vision, invisibility, levitation) need nothing here: the MobEffect packet is
/// all they take. The rest are numbers other systems ask for.
/// </summary>
static class EffectRules
{
    /// <summary>Digging speed: Haste adds a fifth per level, Mining Fatigue keeps 0.3 to the power of the level.</summary>
    public static double DigSpeed(Player.Player player)
    {
        var speed = 1.0 + 0.2 * player.Effects.LevelOf(EffectType.Haste);
        var fatigue = player.Effects.LevelOf(EffectType.MiningFatigue);
        if (fatigue > 0) speed *= Math.Pow(0.3, Math.Min(fatigue, 4));
        return speed;
    }

    /// <summary>Added to the damage of a melee hit: Strength gives 3 a level, Weakness takes 4 a level.</summary>
    public static float MeleeBonus(Player.Player player) =>
        3f * player.Effects.LevelOf(EffectType.Strength) - 4f * player.Effects.LevelOf(EffectType.Weakness);

    /// <summary>What is left of a hit after Resistance: a fifth less per level, none at level 5. The void and hunger ignore it.</summary>
    public static float AfterResistance(Player.Player player, DamageSource source, float amount)
    {
        if (source.Cause is DamageCause.Void or DamageCause.Starve) return amount;
        var level = player.Effects.LevelOf(EffectType.Resistance);
        return level <= 0 ? amount : MathF.Max(0f, amount * (1f - 0.2f * level));
    }

    public static bool ImmuneToFire(Player.Player player) => player.Effects.LevelOf(EffectType.FireResistance) > 0;

    public static bool BreathesUnderWater(Player.Player player) => player.Effects.LevelOf(EffectType.WaterBreathing) > 0;
}

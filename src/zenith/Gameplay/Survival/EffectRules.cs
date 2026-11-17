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

    /// <summary>Added to the damage of a melee hit: what the effects do to the <c>minecraft:attack_damage</c> attribute (Strength +3 a level, Weakness -4).</summary>
    public static float MeleeBonus(Player.Player player) => MeleeBonus(player.Body);

    public static float MeleeBonus(Body body)
    {
        var attack = body.Attributes[Attr.AttackDamage];
        return attack.Unclamped - attack.Base;
    }

    /// <summary>What is left of a hit after Resistance: a fifth less per level, none at level 5. The void and hunger ignore it.</summary>
    public static float AfterResistance(Player.Player player, DamageSource source, float amount) => AfterResistance(player.Body, source, amount);

    public static float AfterResistance(Body body, DamageSource source, float amount)
    {
        if (!source.Cause.ReducedByResistance()) return amount;
        var level = body.Effects.LevelOf(EffectType.Resistance);
        return level <= 0 ? amount : MathF.Max(0f, amount * (1f - 0.2f * level));
    }

    public static bool ImmuneToFire(Player.Player player) => ImmuneToFire(player.Body);

    public static bool ImmuneToFire(Body body) => body.Effects.LevelOf(EffectType.FireResistance) > 0;

    public static bool BreathesUnderWater(Player.Player player) => player.Effects.LevelOf(EffectType.WaterBreathing) > 0;
}

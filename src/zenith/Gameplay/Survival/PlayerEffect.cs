using Zenith.Gameplay.Permissions;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// The first concrete effect slice (Phase XI.3). Values are the Bedrock wire effect ids directly —
/// there is no separate domain↔wire lookup table for two entries. Extend only when a real gameplay
/// consumer (item, movement rule, mob attack) needs the next effect.
/// </summary>
enum EffectType
{
    Speed = 1,
    Slowness = 2,
    Haste = 3,
    MiningFatigue = 4,
    Strength = 5,
    InstantHealth = 6,
    InstantDamage = 7,
    JumpBoost = 8,
    Nausea = 9,
    Regeneration = 10,
    Resistance = 11,
    FireResistance = 12,
    WaterBreathing = 13,
    Invisibility = 14,
    Blindness = 15,
    NightVision = 16,
    Hunger = 17,
    Weakness = 18,
    Poison = 19,
    Wither = 20,
    Saturation = 23,
    Levitation = 24
}

/// <summary>One authoritative timed effect instance. GameLoop-owned; no packet knowledge.</summary>
readonly record struct ActiveEffect(EffectType Type, int Amplifier, ulong ExpiresAtTick)
{
    public bool HasExpired(ulong currentTick) => currentTick >= ExpiresAtTick;
}

/// <summary>Network-to-gameplay handoff for one effect command — apply/refresh or clear-all.</summary>
readonly record struct EffectIntent(bool ClearAll, EffectType Type, int Amplifier, int DurationTicks, Authorization? Issuer = null)
{
    public static EffectIntent Give(EffectType type, int amplifier, int durationTicks, Authorization? issuer = null) =>
        new(false, type, amplifier, durationTicks, issuer);

    /// <summary>Clear every effect. <paramref name="issuer"/> is who asked (null: the server itself).</summary>
    public static EffectIntent ClearBy(Authorization? issuer) => new(true, default, 0, 0, issuer);

    public static EffectIntent Clear => ClearBy(null);
}

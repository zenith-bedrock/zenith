using Zenith.Gameplay.Permissions;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// The first concrete effect slice (Phase XI.3). Values are the Bedrock wire effect ids directly —
/// there is no separate domain↔wire lookup table for two entries. Extend only when a real gameplay
/// consumer (item, movement rule, mob attack) needs the next effect.
/// </summary>
enum EffectType
{
    Regeneration = 10,
    Poison = 19
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

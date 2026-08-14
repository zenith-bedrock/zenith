namespace Zenith.Gameplay;

/// <summary>
/// Phase XXIII — small shared yaw helper, extracted after the exact same
/// <c>MathF.Atan2(-dx, dz) * (180f / MathF.PI)</c> formula was found duplicated identically across
/// Zombie/Skeleton/Spider/Cow/Minecart (five real consumers, past the "≥3" bar
/// docs/history/phases/phase-xxiii-entity-fidelity-findings.md's Part 32 sets for extraction).
///
/// Coordinate convention (documented once here instead of re-derived per call site): yaw 0° faces
/// +Z, yaw increases clockwise when viewed from above (+X is yaw -90°, -Z is yaw ±180°, -X is yaw
/// +90°). This was reverse-engineered from the pre-existing formula and confirmed self-consistent
/// with <c>ProjectileSystemTests</c>'s existing "<c>owner.Yaw = -90f; // +X</c>" fixture — it was
/// not changed, only named and centralized.
/// </summary>
static class LookMath
{
    /// <summary>
    /// Vanilla mobs visibly turn over several ticks rather than snapping instantly to face a new
    /// direction (well-established observed behavior); no live-client measurement was performed
    /// this phase (see docs/entity-fidelity.md, confidence MEDIUM), so this value is a reasonable
    /// placeholder pending Part 36 real-client validation, not a measured constant.
    /// </summary>
    public const float DefaultMaxTurnDegreesPerTick = 10f;

    /// <summary>Yaw (degrees, Zenith convention) an actor at the origin would need to face the direction (dx, dz). Callers normalize/pass a non-zero vector; this does not itself guard against (0, 0).</summary>
    public static float YawTowards(float dx, float dz) => MathF.Atan2(-dx, dz) * (180f / MathF.PI);

    /// <summary>Wraps a yaw value into (-180, 180].</summary>
    public static float NormalizeYaw(float yaw)
    {
        yaw %= 360f;
        if (yaw <= -180f) yaw += 360f;
        else if (yaw > 180f) yaw -= 360f;
        return yaw;
    }

    /// <summary>Shortest signed angular delta from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
    public static float ShortestYawDelta(float from, float to) => NormalizeYaw(to - from);

    /// <summary>
    /// Turns <paramref name="current"/> toward <paramref name="desired"/> by at most
    /// <paramref name="maxDeltaPerTick"/> degrees, always via the shortest angular path (so 179° to
    /// -179° turns ~2°, not ~358°). Returns the desired yaw unchanged once within reach.
    /// </summary>
    public static float MoveYawTowards(float current, float desired, float maxDeltaPerTick)
    {
        var delta = ShortestYawDelta(current, desired);
        if (MathF.Abs(delta) <= maxDeltaPerTick) return NormalizeYaw(desired);
        return NormalizeYaw(current + MathF.CopySign(maxDeltaPerTick, delta));
    }
}

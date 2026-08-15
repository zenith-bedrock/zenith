namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXIII — shared move-replication dedup state, extracted after the exact same
/// <c>ProjectedPosition</c> record (position-only epsilon comparison) was found duplicated
/// identically across Zombie/Cow/Creeper/Golem/Spider/Villager/Minecart (seven consumers). Gained a
/// <see cref="Yaw"/> field this phase: the old position-only comparison could silently drop a
/// pure-rotation update (a mob turning without moving far enough to cross the position epsilon)
/// alongside the movement-replication rotation bug fixed in the same pass — see
/// docs/history/phases/phase-xxiii-entity-fidelity-findings.md.
/// </summary>
readonly record struct ProjectedPose(float X, float Y, float Z, float Yaw)
{
    private const float PositionEpsilonSquared = 0.0001f;
    private const float YawEpsilon = 0.5f;

    public bool MeaningfullyChanged(ProjectedPose previous)
    {
        var dx = X - previous.X;
        var dy = Y - previous.Y;
        var dz = Z - previous.Z;
        if (dx * dx + dy * dy + dz * dz > PositionEpsilonSquared) return true;
        return MathF.Abs(LookMath.ShortestYawDelta(previous.Yaw, Yaw)) > YawEpsilon;
    }
}

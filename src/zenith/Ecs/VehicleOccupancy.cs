namespace Zenith.Ecs;

/// <summary>
/// Phase XXI — Minecart's feature-specific ECS state: the rider relationship (Phase XX). Kept
/// separate from the shared components because no other migrated actor has this concept —
/// forcing it onto <see cref="EntityRuntime"/> as a shared component just to avoid one small
/// per-system store would relocate proliferation, not remove it (see the Phase XXI addendum).
/// </summary>
struct VehicleOccupancy
{
    /// <summary>The rider's <c>RuntimeId</c>, or null if unoccupied — same convention as every mob's target-identity field.</summary>
    public long? OccupantPlayerRuntimeId;
}

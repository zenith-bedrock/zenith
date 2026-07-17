using Zenith.Geometry;

namespace Zenith.World;

/// <summary>
/// Bedrock standing / item entity hitboxes for proximity (ADR §26). Builds <see cref="Aabb"/> values.
/// </summary>
static class EntityHitboxes
{
    public const float PlayerWidth = 0.6f;
    public const float PlayerHeight = 1.8f;
    public const float ItemEntitySize = 0.25f;

    /// <summary>Player BB expand for item pickup (PocketMine / wiki).</summary>
    public static readonly (float X, float Y, float Z) PickupExpand = (1f, 0.5f, 1f);

    /// <summary>Standing player AABB from feet position.</summary>
    public static Aabb PlayerStanding(float feetX, float feetY, float feetZ) =>
        Aabb.FromCenterSize(feetX, feetY, feetZ, PlayerWidth, PlayerHeight);

    /// <summary>Item entity AABB at floor-drop cell (feet at integer Y, center XZ).</summary>
    public static Aabb ItemAtCell(int x, int y, int z) =>
        Aabb.FromCenterSize(x + 0.5f, y, z + 0.5f, ItemEntitySize, ItemEntitySize);
}

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

    /// <summary>
    /// Client player model anchor for <c>MoveActorAbsolute</c> / local <c>MovePlayer</c>:
    /// wire Y = feet + this offset. <see cref="Packets.AddPlayerPacket"/> uses bare feet.
    /// Value is <see cref="Blocks.PlayerEyeHeight"/> + 0.001 so Absolute does not settle
    /// the body slightly into the block top (float error / underground clip).
    /// </summary>
    public const float PlayerNetworkOffset = 1.621f;

    /// <summary>Domain feet → Absolute / MovePlayer wire Y.</summary>
    public static float AbsoluteWireY(float feetY) => feetY + PlayerNetworkOffset;

    /// <summary>Pickup reach: expand standing player AABB by this amount on each axis.</summary>
    public static readonly (float X, float Y, float Z) PickupExpand = (1f, 0.5f, 1f);

    /// <summary>Standing player AABB from feet position.</summary>
    public static Aabb PlayerStanding(float feetX, float feetY, float feetZ) =>
        Aabb.FromCenterSize(feetX, feetY, feetZ, PlayerWidth, PlayerHeight);

    /// <summary>Item entity AABB at floor-drop cell (feet at integer Y, center XZ).</summary>
    public static Aabb ItemAtCell(int x, int y, int z) =>
        Aabb.FromCenterSize(x + 0.5f, y, z + 0.5f, ItemEntitySize, ItemEntitySize);
}

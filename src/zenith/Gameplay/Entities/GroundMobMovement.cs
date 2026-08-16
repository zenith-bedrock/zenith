using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXIX rewrite: replaces the old "non-air == support" probe (any non-air block, including
/// water, counted as solid ground — a ground mob standing over a lake read as standing on land) with
/// real physical semantics (<see cref="Blocks.CanSupportGroundActor"/>) plus a small deterministic
/// vertical model (gravity, falling, landing, one-block step-up). Still a pure resolution predicate
/// over primitives — it does not know what a "mob" is, does not choose a heading, and never touches
/// replication. Both the ECS roster (Position + Velocity.Y reused as a fall-speed magnitude) and the
/// legacy roster (a concrete PositionY + a small per-mob fall-speed field) call the same two static
/// methods; neither needed to migrate for the other to gain physics. See
/// docs/history/phases/phase-xxix-ground-actor-physics-findings.md for the research behind the
/// constants and the behavioral decisions this makes.
/// </summary>
static class GroundMobMovement
{
    /// <summary>
    /// Per-tick downward acceleration, blocks/tick². Vanilla living-entity gravity — corroborated
    /// independently by PocketMine-MP (<c>Living::getInitialGravity</c>) and Basalt
    /// (<c>EntityMovementTrait.GravityPerTick</c>). Deliberately distinct from
    /// <see cref="WorldInteraction.GravitySystem"/>'s falling-block gravity (0.04) — sand/gravel and
    /// living entities have genuinely different fall rates in vanilla, not an inconsistency to unify.
    /// </summary>
    public const float Gravity = 0.08f;

    /// <summary>Per-tick fall-speed retention factor (drag = 0.02) — same sources as <see cref="Gravity"/>.</summary>
    public const float Drag = 0.98f;

    /// <summary>Magnitude cap on downward fall speed, blocks/tick — same sources as <see cref="Gravity"/>.</summary>
    public const float TerminalFallSpeed = 3.92f;

    /// <summary>
    /// AI's chosen horizontal destination, resolved against real physical occupancy. Deliberately
    /// carries no support requirement — Phase XXIX explicitly allows a mob to walk off a ledge or
    /// down a slope; whether it then falls is <see cref="ResolveVertical"/>'s job on the next tick,
    /// not this one's gate. Tries the destination at the current Y first (flat ground, descending
    /// terrain), then one block higher (one-block step-up, general two-cell retry — not a special
    /// case limited to "exactly one block tall") before failing. <paramref name="resolvedY"/> equals
    /// <paramref name="y"/> unless a step-up was taken.
    /// </summary>
    public static bool TryMoveHorizontal(World.World world, float x, float y, float z, float desiredX, float desiredZ, out float resolvedY)
    {
        var feetY = (int)MathF.Floor(y);
        if (IsClear(world, desiredX, feetY, desiredZ) && IsClear(world, desiredX, feetY + 1, desiredZ))
        {
            resolvedY = y;
            return true;
        }

        var steppedFeetY = feetY + 1;
        if (IsClear(world, desiredX, steppedFeetY, desiredZ) && IsClear(world, desiredX, steppedFeetY + 1, desiredZ) &&
            IsClear(world, x, steppedFeetY, z) && IsClear(world, x, steppedFeetY + 1, z))
        {
            resolvedY = steppedFeetY;
            return true;
        }

        resolvedY = y;
        return false;
    }

    /// <summary>
    /// One tick of gravity: applies while unsupported, halts and lands the instant the fall crosses a
    /// supporting block's top face — gradual acceleration with a per-tick landing check, matching
    /// every reference implementation consulted (none snap-teleport a fall). Returns whether
    /// <paramref name="y"/> changed. <paramref name="fallSpeed"/> is caller-owned state (ECS:
    /// <c>Velocity.Y</c> reused as a downward-speed magnitude, matching <see cref="WorldInteraction.GravitySystem"/>'s
    /// own <c>FallingBlockEntry.VelocityY</c> convention; legacy: a small per-mob field) — this method
    /// only ever reads/writes it and <paramref name="y"/>, never decides whether it should be called.
    /// </summary>
    public static bool ResolveVertical(World.World world, float x, float z, ref float y, ref float fallSpeed, out bool grounded)
    {
        var blockX = (int)MathF.Floor(x);
        var blockZ = (int)MathF.Floor(z);
        var feetY = (int)MathF.Floor(y);

        if (fallSpeed <= 0f && Blocks.CanSupportGroundActor(world.GetBlock(blockX, feetY - 1, blockZ)))
        {
            fallSpeed = 0f;
            grounded = true;
            return false;
        }

        fallSpeed += Gravity;
        fallSpeed *= Drag;
        if (fallSpeed > TerminalFallSpeed) fallSpeed = TerminalFallSpeed;

        var candidateY = y - fallSpeed;
        var candidateFeetY = (int)MathF.Floor(candidateY);

        for (var checkY = feetY - 1; checkY >= candidateFeetY; checkY--)
        {
            if (!Blocks.CanSupportGroundActor(world.GetBlock(blockX, checkY, blockZ))) continue;
            y = checkY + 1;
            fallSpeed = 0f;
            grounded = true;
            return true;
        }

        y = candidateY;
        grounded = false;
        return true;
    }

    /// <summary>
    /// Narrow support-and-occupancy check for movers that carry no vertical model of their own —
    /// Minecart's velocity-rolling fallback and Enderman's teleport-landing validation, neither of
    /// which walk tick-by-tick or fall. Same shape as the pre-Phase-XXIX check (literal air occupancy,
    /// real solid support underneath), with only the water-as-support bug fixed. Movers with genuine
    /// continuous walking use <see cref="TryMoveHorizontal"/>/<see cref="ResolveVertical"/> instead.
    /// </summary>
    public static bool IsSupportedGroundCell(World.World world, float x, float y, float z)
    {
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        return Blocks.IsAir(world.GetBlock(blockX, blockY, blockZ)) &&
               Blocks.IsAir(world.GetBlock(blockX, blockY + 1, blockZ)) &&
               Blocks.CanSupportGroundActor(world.GetBlock(blockX, blockY - 1, blockZ));
    }

    private static bool IsClear(World.World world, float x, int y, float z) =>
        Blocks.CanOccupy(world.GetBlock((int)MathF.Floor(x), y, (int)MathF.Floor(z)));
}

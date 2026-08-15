namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XVI — age-based despawn ("no player has been near this mob for a while, remove it") was
/// added to all five ground mob systems simultaneously, all needing the identical check, which is
/// stronger evidence than the "third instance" bar <see cref="GroundMobMovement"/> used. Deliberately
/// a pure decision function, not a store or a mob type: it reads a position/tick pair and returns a
/// decision plus the updated "last seen" tick: the caller still owns the field, the actual removal,
/// and any replication cleanup — the same ownership split <see cref="GroundMobCombat"/> uses for
/// store removal. This is the third small orthogonal primitive (combat, movement, lifecycle) rather
/// than one primitive growing three responsibilities — see
/// docs/history/phases/phase-xvi-entity-runtime-findings.md for why that distinction matters.
/// </summary>
static class DespawnLifecycle
{
    /// <summary>
    /// Vanilla-parity despawn window (5 min @ 20 TPS) — deliberately the same constant
    /// <see cref="World.FloorDropStore.DefaultDespawnTicks"/> already used for uncollected floor
    /// drops, a completely independent, pre-existing age-based expiry in a different runtime
    /// category. Not shared code — the same number chosen twice for the same reason.
    /// </summary>
    public const ulong DefaultDespawnTicks = 6000;

    /// <summary>
    /// Returns whether the mob should despawn now, and the "last seen near a player" tick the
    /// caller should persist regardless (refreshed to <paramref name="currentTick"/> whenever a
    /// player is within range).
    /// </summary>
    public static (bool ShouldDespawn, ulong LastSeenNearPlayerTick) EvaluateDespawn(
        float positionX,
        float positionZ,
        IReadOnlyList<Player.Player> online,
        float despawnRadius,
        ulong currentTick,
        ulong lastSeenNearPlayerTick,
        ulong despawnTicks = DefaultDespawnTicks)
    {
        var radiusSquared = despawnRadius * despawnRadius;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = player.PositionX - positionX;
            var dz = player.PositionZ - positionZ;
            if (dx * dx + dz * dz <= radiusSquared)
                return (false, currentTick);
        }

        return (currentTick - lastSeenNearPlayerTick >= despawnTicks, lastSeenNearPlayerTick);
    }
}

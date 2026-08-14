namespace Zenith.Gameplay;

/// <summary>
/// Second "fundamentally different behavior" ground mob (Phase XV) — validates whether
/// <see cref="IDamageableActor"/>/<see cref="GroundMobCombat"/> survive a mob whose movement is
/// teleportation (never <see cref="GroundMobMovement"/>'s incremental step probe, though it does
/// reuse the same position-validity check for teleport destinations) and whose aggression is
/// damage-triggered rather than proximity-triggered. All of that stays local to this type and
/// EndermanSystem; GroundMobCombat never sees it.
/// </summary>
sealed class Enderman : IDamageableActor
{
    public Enderman(long entityId, ulong runtimeId, float x, float y, float z)
    {
        EntityId = entityId;
        RuntimeId = runtimeId;
        PositionX = x;
        PositionY = y;
        PositionZ = z;
        Health = new HealthState(20f); // Simplified from vanilla's 40 for parity with the other slices.
    }

    public long EntityId { get; }
    public ulong RuntimeId { get; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Yaw { get; set; }
    public HealthState Health { get; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Player who last hit this Enderman. EndermanSystem-owned; cleared when aggro expires.</summary>
    internal long? AggroTargetRuntimeId { get; set; }

    /// <summary>Counts down each tick; teleport-chase-and-retaliate while positive, passive-teleport-wander otherwise.</summary>
    internal int AggroTicksRemaining { get; set; }

    /// <summary>Next tick a passive (non-aggro) teleport may occur.</summary>
    internal ulong NextPassiveTeleportTick { get; set; }

    /// <summary>Next tick a retaliation melee swing may land, while aggro'd.</summary>
    internal ulong NextAttackTick { get; set; }

    /// <summary>Phase XVI: last tick a player was within despawn range — see <see cref="DespawnLifecycle"/>.</summary>
    internal ulong LastSeenNearPlayerTick { get; set; }

    public DamageResult ApplyDamage(DamageSource source, float amount, ulong currentTick) => Health.Apply(source, amount, currentTick);
    public void Remove() => IsActive = false;
}

/// <summary>Concrete active-enderman store owned by <see cref="Systems.EndermanSystem"/>.</summary>
sealed class EndermanStore
{
    private readonly List<Enderman> _active = new();
    public IReadOnlyList<Enderman> Active => _active;

    public bool TryAdd(Enderman enderman)
    {
        ArgumentNullException.ThrowIfNull(enderman);
        if (!enderman.IsActive || _active.Contains(enderman)) return false;
        _active.Add(enderman);
        return true;
    }

    public void Remove(Enderman enderman) => _active.Remove(enderman);
}

namespace Zenith.Gameplay;

/// <summary>
/// Phase XVII — the first entity that does not fit the ground-mob movement assumption. Structurally
/// still an <see cref="IDamageableActor"/> (identity + position + health + damage + removal is
/// movement-agnostic, despite the interface's name), but its movement is flight: no supporting
/// block required, wanders in three dimensions. See BatSystem for the bespoke flight-validity
/// check — deliberately NOT <see cref="GroundMobMovement"/>, and deliberately not extracted into a
/// new shared primitive, since this is the only flying mob so far.
/// </summary>
sealed class Bat : IDamageableActor
{
    public Bat(long entityId, ulong runtimeId, float x, float y, float z)
    {
        EntityId = entityId;
        RuntimeId = runtimeId;
        PositionX = x;
        PositionY = y;
        PositionZ = z;
        Health = new HealthState(6f); // Vanilla-parity: bats are fragile.
    }

    public long EntityId { get; }
    public ulong RuntimeId { get; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Yaw { get; set; }
    public HealthState Health { get; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Concrete BatSystem wander state — a heading in 3D, unlike every ground mob's 2D heading.</summary>
    internal float WanderDirectionX { get; set; }
    internal float WanderDirectionY { get; set; }
    internal float WanderDirectionZ { get; set; }
    internal ulong WanderChangeAtTick { get; set; }

    /// <summary>Last tick a player was within despawn range — see <see cref="DespawnLifecycle"/>.</summary>
    internal ulong LastSeenNearPlayerTick { get; set; }

    public DamageResult ApplyDamage(DamageSource source, float amount) => Health.Apply(source, amount);
    public void Remove() => IsActive = false;
}

/// <summary>Concrete active-bat store owned by <see cref="Systems.BatSystem"/> — same shape as every other ground-mob store.</summary>
sealed class BatStore
{
    private readonly List<Bat> _active = new();
    public IReadOnlyList<Bat> Active => _active;

    public bool TryAdd(Bat bat)
    {
        ArgumentNullException.ThrowIfNull(bat);
        if (!bat.IsActive || _active.Contains(bat)) return false;
        _active.Add(bat);
        return true;
    }

    public void Remove(Bat bat) => _active.Remove(bat);
}

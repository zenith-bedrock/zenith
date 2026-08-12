namespace Zenith.Gameplay;

/// <summary>The first living actor is concrete; this is not an entity hierarchy.</summary>
sealed class Zombie
{
    public Zombie(long entityId, ulong runtimeId, float x, float y, float z)
    {
        EntityId = entityId;
        RuntimeId = runtimeId;
        PositionX = x;
        PositionY = y;
        PositionZ = z;
        Health = new HealthState(20f);
    }

    public long EntityId { get; }
    public ulong RuntimeId { get; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Yaw { get; set; }
    /// <summary>Concrete target identity retained by ZombieSystem while the player remains valid.</summary>
    public long? TargetPlayerRuntimeId { get; internal set; }
    public HealthState Health { get; }
    public bool IsActive { get; private set; } = true;

    public DamageResult ApplyDamage(DamageSource source, float amount) => Health.Apply(source, amount);
    public void Remove() => IsActive = false;
}

/// <summary>Concrete active-zombie store owned by <see cref="Systems.ZombieSystem"/>.</summary>
sealed class ZombieStore
{
    private readonly List<Zombie> _active = new();
    public IReadOnlyList<Zombie> Active => _active;

    public bool TryAdd(Zombie zombie)
    {
        ArgumentNullException.ThrowIfNull(zombie);
        if (!zombie.IsActive || _active.Contains(zombie)) return false;
        _active.Add(zombie);
        return true;
    }

    public void Remove(Zombie zombie) => _active.Remove(zombie);
}

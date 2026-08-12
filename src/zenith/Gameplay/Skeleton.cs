namespace Zenith.Gameplay;

/// <summary>Concrete ranged mob state; intentionally separate from Zombie.</summary>
sealed class Skeleton
{
    public Skeleton(long entityId, ulong runtimeId, float x, float y, float z)
    {
        EntityId = entityId; RuntimeId = runtimeId; PositionX = x; PositionY = y; PositionZ = z;
        Health = new HealthState(20f);
    }
    public long EntityId { get; }
    public ulong RuntimeId { get; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Yaw { get; set; }
    public HealthState Health { get; }
    public bool IsActive { get; private set; } = true;
    public void Remove() => IsActive = false;

    public DamageResult ApplyDamage(DamageSource source, float amount) => Health.Apply(source, amount);
}

sealed class SkeletonStore
{
    private readonly List<Skeleton> _active = [];
    public IReadOnlyList<Skeleton> Active => _active;
    public bool TryAdd(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (!skeleton.IsActive || _active.Contains(skeleton)) return false;
        _active.Add(skeleton); return true;
    }
    public void Remove(Skeleton skeleton) => _active.Remove(skeleton);
}

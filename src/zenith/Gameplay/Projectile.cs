namespace Zenith.Gameplay;

/// <summary>
/// Concrete short-lived projectile state for the second actor vertical slice. This storage shape
/// is intentionally scaffolding: it records pressure alongside ZombieStore/FallingBlockStore,
/// rather than defining a reusable actor contract.
/// </summary>
sealed class Projectile
{
    public Projectile(long entityId, ulong runtimeId, long ownerRuntimeId, float x, float y, float z, float velocityX, float velocityY, float velocityZ)
    {
        EntityId = entityId;
        RuntimeId = runtimeId;
        OwnerRuntimeId = ownerRuntimeId;
        PositionX = x;
        PositionY = y;
        PositionZ = z;
        VelocityX = velocityX;
        VelocityY = velocityY;
        VelocityZ = velocityZ;
    }

    public long EntityId { get; }
    public ulong RuntimeId { get; }
    public long OwnerRuntimeId { get; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float VelocityX { get; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; }
    public int AgeTicks { get; set; }
    public bool IsActive { get; private set; } = true;

    public void Remove() => IsActive = false;
}

/// <summary>Concrete active projectile storage, owned by ProjectileSystem on the GameLoop.</summary>
sealed class ProjectileStore
{
    internal const int SoftCap = 2048;
    private readonly List<Projectile> _active = [];
    public IReadOnlyList<Projectile> Active => _active;

    public bool TryAdd(Projectile projectile)
    {
        ArgumentNullException.ThrowIfNull(projectile);
        if (!projectile.IsActive || _active.Count >= SoftCap || _active.Contains(projectile)) return false;
        _active.Add(projectile);
        return true;
    }

    public void Remove(Projectile projectile) => _active.Remove(projectile);
}

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// First "different death behavior" ground mob (Phase XV) — validates whether
/// <see cref="IDamageableActor"/>/<see cref="GroundMobCombat"/> survive a mob whose primary threat is a
/// self-triggered area-damage explosion, not a melee/ranged attack. Fuse/explosion state is
/// entirely local to this type and CreeperSystem; GroundMobCombat never sees it.
/// </summary>
sealed class Creeper : IDamageableActor
{
    public Creeper(long entityId, ulong runtimeId, float x, float y, float z)
    {
        EntityId = entityId;
        RuntimeId = runtimeId;
        PositionX = x;
        PositionY = y;
        PositionZ = z;
        Health = new HealthState(20f); // Vanilla-parity creeper health.
    }

    public long EntityId { get; }
    public ulong RuntimeId { get; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Yaw { get; set; }
    public HealthState Health { get; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Phase XXIX: downward fall-speed magnitude — the legacy-roster equivalent of an ECS actor's <c>Velocity.Y</c>.</summary>
    internal float VerticalFallSpeed { get; set; }

    /// <summary>Concrete target identity — same shape as Zombie's, CreeperSystem-owned.</summary>
    internal long? TargetPlayerRuntimeId { get; set; }

    /// <summary>True from ignite until explode-or-defuse. CreeperSystem-owned; no meaning outside it.</summary>
    internal bool IsFusing { get; set; }

    /// <summary>Tick the fuse was (re)started; explosion fires <c>FuseDurationTicks</c> later.</summary>
    internal ulong FuseStartedTick { get; set; }

    /// <summary>Phase XVI: last tick a player was within despawn range — see <see cref="DespawnLifecycle"/>.</summary>
    internal ulong LastSeenNearPlayerTick { get; set; }

    public DamageResult ApplyDamage(DamageSource source, float amount, ulong currentTick) => Health.Apply(source, amount, currentTick);
    public void Remove() => IsActive = false;
}

/// <summary>Concrete active-creeper store owned by <see cref="Systems.CreeperSystem"/>.</summary>
sealed class CreeperStore
{
    private readonly List<Creeper> _active = new();
    public IReadOnlyList<Creeper> Active => _active;

    public bool TryAdd(Creeper creeper)
    {
        ArgumentNullException.ThrowIfNull(creeper);
        if (!creeper.IsActive || _active.Contains(creeper)) return false;
        _active.Add(creeper);
        return true;
    }

    public void Remove(Creeper creeper) => _active.Remove(creeper);
}

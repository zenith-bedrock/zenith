using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XVIII, Priority 4 — a boss-shaped pressure test: high health, a health-threshold phase
/// transition, and a second attack (an area slam) alongside melee. Deliberately does NOT retain a
/// target field the way <see cref="Zombie"/>/<see cref="Spider"/> do — see GolemSystem's targeting,
/// which re-scans the nearest player every tick instead. That is a real behavioral difference, not
/// an attempt to dodge the "third instance" question: a boss re-evaluating its target every tick
/// (so it always focuses whoever is closest right now, mid-fight) is a materially different design
/// than a chase mob that commits to a target and only drops it when it becomes invalid. See
/// docs/history/phases/phase-xviii-runtime-pressure-findings.md for what this did and did not prove about
/// GroundMobTargeting.
/// </summary>
sealed class Golem : IDamageableActor
{
    public Golem(long entityId, ulong runtimeId, float x, float y, float z)
    {
        EntityId = entityId;
        RuntimeId = runtimeId;
        PositionX = x;
        PositionY = y;
        PositionZ = z;
        Health = new HealthState(100f);
    }

    public long EntityId { get; }
    public ulong RuntimeId { get; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Yaw { get; set; }
    public HealthState Health { get; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Phase transition state — GolemSystem-owned, checked once per tick against a health fraction.</summary>
    internal bool IsEnraged { get; set; }

    /// <summary>Next tick the area slam ability may fire, only reachable once <see cref="IsEnraged"/>.</summary>
    internal ulong NextSlamTick { get; set; }

    /// <summary>Next tick a melee swing may land.</summary>
    internal ulong NextAttackTick { get; set; }

    /// <summary>Last tick a player was within despawn range — see <see cref="DespawnLifecycle"/>.</summary>
    internal ulong LastSeenNearPlayerTick { get; set; }

    /// <summary>
    /// Phase XXIII-B — vanilla parity: naturally-passive iron golems only become hostile toward a
    /// specific player who provoked them (attacked the golem itself, or attacked a villager nearby).
    /// Retained once set, same target-retention shape as Zombie/Spider, until that player is no
    /// longer a valid target.
    /// </summary>
    internal long? TargetPlayerRuntimeId { get; set; }

    public DamageResult ApplyDamage(DamageSource source, float amount, ulong currentTick) => Health.Apply(source, amount, currentTick);
    public void Remove() => IsActive = false;
}

/// <summary>Concrete active-golem store owned by <see cref="Systems.GolemSystem"/> — same shape as every other ground-mob store.</summary>
sealed class GolemStore
{
    private readonly List<Golem> _active = new();
    public IReadOnlyList<Golem> Active => _active;

    public bool TryAdd(Golem golem)
    {
        ArgumentNullException.ThrowIfNull(golem);
        if (!golem.IsActive || _active.Contains(golem)) return false;
        _active.Add(golem);
        return true;
    }

    public void Remove(Golem golem) => _active.Remove(golem);
}

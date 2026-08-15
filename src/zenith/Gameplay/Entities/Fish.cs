using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XX, Priority 2 — the second non-ground-navigation mob (after Bat, Phase XVII), and the
/// first with a THIRD distinct movement-validity rule: unlike <see cref="GroundMobMovement"/>'s
/// "solid block below, air above" or Bat's "air above and at the body, no support needed," a fish
/// requires the destination cell itself to be water (<see cref="Systems.FishSystem.TrySwim"/>).
/// Still implements <see cref="IDamageableActor"/> and reuses <see cref="GroundMobCombat"/>/
/// <see cref="DespawnLifecycle"/> unmodified — a sixth and eleventh confirmation respectively
/// that those contracts are movement-mode-agnostic. See
/// docs/history/phases/phase-xx-runtime-relationships-findings.md for the three-way movement-model comparison
/// this was built to produce.
/// </summary>
sealed class Fish : IDamageableActor
{
    public Fish(long entityId, ulong runtimeId, float x, float y, float z)
    {
        EntityId = entityId;
        RuntimeId = runtimeId;
        PositionX = x;
        PositionY = y;
        PositionZ = z;
        Health = new HealthState(3f); // Vanilla-parity: fish are fragile.
    }

    public long EntityId { get; }
    public ulong RuntimeId { get; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Yaw { get; set; }
    public HealthState Health { get; }
    public bool IsActive { get; private set; } = true;

    /// <summary>3D wander heading — same shape as Bat's, different validity rule entirely.</summary>
    internal float WanderDirectionX { get; set; }
    internal float WanderDirectionY { get; set; }
    internal float WanderDirectionZ { get; set; }
    internal ulong WanderChangeAtTick { get; set; }

    /// <summary>Last tick a player was within despawn range — see <see cref="DespawnLifecycle"/>.</summary>
    internal ulong LastSeenNearPlayerTick { get; set; }

    public DamageResult ApplyDamage(DamageSource source, float amount, ulong currentTick) => Health.Apply(source, amount, currentTick);
    public void Remove() => IsActive = false;
}

/// <summary>Concrete active-fish store owned by <see cref="Systems.FishSystem"/> — same shape as every other ground-mob-shaped store.</summary>
sealed class FishStore
{
    private readonly List<Fish> _active = new();
    public IReadOnlyList<Fish> Active => _active;

    public bool TryAdd(Fish fish)
    {
        ArgumentNullException.ThrowIfNull(fish);
        if (!fish.IsActive || _active.Contains(fish)) return false;
        _active.Add(fish);
        return true;
    }

    public void Remove(Fish fish) => _active.Remove(fish);
}

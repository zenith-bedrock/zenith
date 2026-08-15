using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XVII, Priority 3 — the first non-player entity carrying inventory-shaped data. Deliberately
/// minimal: <see cref="Wares"/> is a flat list of items the villager "has," not a full
/// <see cref="Player.PlayerInventory"/> (no slots, no hotbar, no equipment). Phase XVIII wired a
/// real (if simple) trade interaction on top of this data — see
/// <see cref="Systems.VillagerSystem"/> and docs/history/phases/phase-xviii-runtime-pressure-findings.md.
/// </summary>
sealed class Villager : IDamageableActor
{
    public Villager(long entityId, ulong runtimeId, float x, float y, float z)
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
    public HealthState Health { get; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Minimal inventory-shaped data: what this villager has to offer. No slot indices, no stack
    /// splitting, no transaction — the smallest thing that lets the architecture question
    /// ("does inventory generalize beyond Player?") be asked with real data instead of guessed at.
    /// </summary>
    internal List<StackId> Wares { get; } = [];

    /// <summary>What this villager wants in exchange for one of <see cref="Wares"/> — Phase XVIII trade.</summary>
    internal StackId RequestedWare { get; set; }

    /// <summary>Next tick a trade may complete — VillagerSystem-owned, same shape as a mob's attack cooldown.</summary>
    internal ulong NextTradeTick { get; set; }

    /// <summary>Wander state — identical shape to Cow's, see docs/history/phases/phase-xvii-runtime-pressure-findings.md.</summary>
    internal float WanderDirectionX { get; set; }
    internal float WanderDirectionZ { get; set; }
    internal ulong WanderChangeAtTick { get; set; }

    /// <summary>Last tick a player was within despawn range — see <see cref="DespawnLifecycle"/>.</summary>
    internal ulong LastSeenNearPlayerTick { get; set; }

    public DamageResult ApplyDamage(DamageSource source, float amount, ulong currentTick) => Health.Apply(source, amount, currentTick);
    public void Remove() => IsActive = false;
}

/// <summary>Concrete active-villager store owned by <see cref="Systems.VillagerSystem"/> — same shape as every other ground-mob store.</summary>
sealed class VillagerStore
{
    private readonly List<Villager> _active = new();
    public IReadOnlyList<Villager> Active => _active;

    public bool TryAdd(Villager villager)
    {
        ArgumentNullException.ThrowIfNull(villager);
        if (!villager.IsActive || _active.Contains(villager)) return false;
        _active.Add(villager);
        return true;
    }

    public void Remove(Villager villager) => _active.Remove(villager);
}

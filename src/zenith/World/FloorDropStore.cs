using System.Threading;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// Floor drops with Bedrock item-entity wire (ADR §26): sparse cells holding a stack
/// + entity runtime id + pickup delay. Pickup uses player AABB expand vs item AABB (PM/wiki).
/// SoftCap refuses new cell keys (lossy vs entities — honest LAN bound).
/// </summary>
sealed class FloorDropStore
{
    public const int MaxStack = 64;
    internal const int SoftCap = 2048;

    /// <summary>Default delay after deposit before pickup (PM <c>dropItem</c> / DF 0.5s).</summary>
    public const int DefaultPickupDelay = 10;

    private readonly Dictionary<(int X, int Y, int Z), DropSlot> _drops = new();
    private readonly ILogger? _logger;
    private int _capWarned;

    readonly record struct DropSlot(int RuntimeId, int Count, long EntityRuntimeId, int PickupDelayTicks);

    /// <summary>Result of a successful deposit (wire fan-out).</summary>
    public readonly record struct DepositResult(
        int X,
        int Y,
        int Z,
        int ItemRuntimeId,
        int Count,
        long EntityRuntimeId,
        bool Created,
        bool CountChanged);

    public FloorDropStore(ILogger? logger = null) => _logger = logger;

    public int Count => _drops.Count;

    /// <summary>Legacy alias — prefer <see cref="TryAddOrMerge"/> when refuse matters.</summary>
    public void AddOrMerge(int x, int y, int z, int runtimeId, int count, long entityRuntimeIdIfNew) =>
        TryAddOrMerge(x, y, z, runtimeId, count, entityRuntimeIdIfNew, out _);

    /// <summary>
    /// Merge into an existing same-item cell, or place a new cell under <see cref="SoftCap"/>.
    /// <paramref name="entityRuntimeIdIfNew"/> is used only when creating a new cell.
    /// Merge delay = <c>max(existing, incoming)</c> (PM). New cell uses <paramref name="pickupDelayTicks"/>.
    /// Returns false when a new cell would exceed the soft cap (existing cells may still merge).
    /// </summary>
    public bool TryAddOrMerge(
        int x,
        int y,
        int z,
        int runtimeId,
        int count,
        long entityRuntimeIdIfNew,
        out DepositResult? deposit,
        int pickupDelayTicks = DefaultPickupDelay)
    {
        deposit = null;
        if (count <= 0 || runtimeId == Blocks.Air) return true;
        var key = (x, y, z);
        if (_drops.TryGetValue(key, out var existing) && existing.RuntimeId == runtimeId)
        {
            var merged = Math.Min(MaxStack, existing.Count + count);
            var delay = Math.Max(existing.PickupDelayTicks, pickupDelayTicks);
            var changed = merged != existing.Count || delay != existing.PickupDelayTicks;
            _drops[key] = new DropSlot(runtimeId, merged, existing.EntityRuntimeId, delay);
            deposit = new DepositResult(x, y, z, runtimeId, merged, existing.EntityRuntimeId, Created: false, CountChanged: merged != existing.Count);
            return true;
        }

        if (!_drops.ContainsKey(key) && _drops.Count >= SoftCap)
        {
            if (Interlocked.Exchange(ref _capWarned, 1) == 0)
            {
                _logger?.Warning(
                    $"FloorDropStore at SoftCap ({SoftCap}): refusing new floor-drop cells.");
            }

            return false;
        }

        var entityId = entityRuntimeIdIfNew;
        var newCount = Math.Min(MaxStack, count);
        _drops[key] = new DropSlot(runtimeId, newCount, entityId, Math.Max(0, pickupDelayTicks));
        deposit = new DepositResult(
            x, y, z, runtimeId, newCount, entityId, Created: true, CountChanged: true);
        return true;
    }

    /// <summary>Decrement pickup delays by <paramref name="tickDiff"/> (call once per GameLoop tick).</summary>
    public void TickPickupDelays(int tickDiff = 1)
    {
        if (tickDiff <= 0 || _drops.Count == 0) return;

        // Copy keys — cannot mutate dictionary while enumerating.
        var keys = new List<(int X, int Y, int Z)>(_drops.Count);
        foreach (var key in _drops.Keys)
            keys.Add(key);

        foreach (var key in keys)
        {
            if (!_drops.TryGetValue(key, out var slot) || slot.PickupDelayTicks <= 0)
                continue;
            var next = slot.PickupDelayTicks - tickDiff;
            if (next < 0) next = 0;
            _drops[key] = slot with { PickupDelayTicks = next };
        }
    }

    public IEnumerable<((int X, int Y, int Z) Pos, int RuntimeId, int Count, long EntityRuntimeId, int PickupDelayTicks)> Snapshot()
    {
        foreach (var (pos, slot) in _drops)
            yield return (pos, slot.RuntimeId, slot.Count, slot.EntityRuntimeId, slot.PickupDelayTicks);
    }

    public bool TryTake(int x, int y, int z, out int runtimeId, out int count, out long entityRuntimeId)
    {
        if (!_drops.Remove((x, y, z), out var slot))
        {
            runtimeId = Blocks.Air;
            count = 0;
            entityRuntimeId = 0;
            return false;
        }

        runtimeId = slot.RuntimeId;
        count = slot.Count;
        entityRuntimeId = slot.EntityRuntimeId;
        return true;
    }

    /// <summary>
    /// Removes up to <paramref name="max"/> from the cell. Full take removes the key;
    /// partial leave updates count (same delay) and returns a <see cref="DepositResult"/> for Remove+Add republish.
    /// </summary>
    public bool TryTakeUpTo(
        int x,
        int y,
        int z,
        int max,
        out int runtimeId,
        out int taken,
        out long entityRuntimeId,
        out DepositResult? remainingPublish)
    {
        runtimeId = Blocks.Air;
        taken = 0;
        entityRuntimeId = 0;
        remainingPublish = null;

        if (max <= 0) return false;
        var key = (x, y, z);
        if (!_drops.TryGetValue(key, out var slot)) return false;

        runtimeId = slot.RuntimeId;
        entityRuntimeId = slot.EntityRuntimeId;
        taken = Math.Min(max, slot.Count);

        if (taken >= slot.Count)
        {
            _drops.Remove(key);
            return true;
        }

        var left = slot.Count - taken;
        // Same cell / same delay — do not reset pickup delay on partial leftover.
        _drops[key] = new DropSlot(slot.RuntimeId, left, slot.EntityRuntimeId, slot.PickupDelayTicks);
        remainingPublish = new DepositResult(
            x, y, z, slot.RuntimeId, left, slot.EntityRuntimeId,
            Created: false, CountChanged: true);
        return true;
    }
}

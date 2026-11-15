using System.Threading;
using Zenith.Logging;

namespace Zenith.World;

/// <summary>
/// Floor drops with Bedrock item-entity wire (ADR §26 / §55): sparse cells holding a
/// <see cref="StackId"/> + entity runtime id + pickup delay.
/// Cell keys are custody handles; moving items carry a separate physical pose used for pickup.
/// SoftCap refuses new cell keys (lossy vs entities — honest LAN bound).
/// </summary>
sealed class FloorDropStore
{
    public const int MaxStack = 64;
    internal const int SoftCap = 2048;

    /// <summary>Ticks after deposit before the drop may be picked up (~0.5s at 20 TPS).</summary>
    public const int DefaultPickupDelay = 10;

    /// <summary>Ticks alive before an uncollected drop despawns (5 min at 20 TPS — vanilla parity).</summary>
    public const int DefaultDespawnTicks = 6000;

    private readonly Dictionary<(int X, int Y, int Z), DropSlot> _drops = new();
    private readonly List<(int X, int Y, int Z)> _delayScratch = new();
    private readonly List<(int X, int Y, int Z)> _ageScratch = new();
    private readonly ILogger? _logger;
    private int _capWarned;

    internal readonly record struct DropSlot(StackId Id, int Count, long EntityRuntimeId, int PickupDelayTicks, int AgeTicks = 0, FloorDropMotion? Motion = null);

    public readonly record struct DropView((int X, int Y, int Z) Key, StackId Id, int Count, long EntityRuntimeId, int PickupDelayTicks, FloorDropMotion Pose, bool HasMotion);

    public List<DropView> SnapshotViews()
    {
        var result = new List<DropView>(_drops.Count);
        CopyViews(result);
        return result;
    }

    public void CopyViews(List<DropView> result)
    {
        result.Clear();
        foreach (var (key, slot) in _drops)
            result.Add(new(key, slot.Id, slot.Count, slot.EntityRuntimeId, slot.PickupDelayTicks,
                slot.Motion ?? new FloorDropMotion(key.X + .5f, key.Y, key.Z + .5f, 0, 0, 0), slot.Motion.HasValue));
    }

    public bool HasEntitiesInChunk(int chunkX, int chunkZ)
    {
        foreach (var (key, slot) in _drops)
        {
            var x = slot.Motion?.X ?? key.X + .5f;
            var z = slot.Motion?.Z ?? key.Z + .5f;
            if (ChunkMath.BlockToChunk(x) == chunkX && ChunkMath.BlockToChunk(z) == chunkZ) return true;
        }
        return false;
    }

    public void UpdateMotion(in DropView drop, FloorDropMotion motion)
    {
        if (_drops.TryGetValue(drop.Key, out var slot) && slot.EntityRuntimeId == drop.EntityRuntimeId)
            _drops[drop.Key] = slot with { Motion = motion };
    }

    public bool CanMergeAt(int x, int y, int z)
    {
        if (!_drops.TryGetValue((x, y, z), out var slot) || slot.Motion is not { } body) return true;
        return body.VelocityX == 0 && body.VelocityY == 0 && body.VelocityZ == 0 &&
            MathF.Abs(body.X - (x + .5f)) < .5f && MathF.Abs(body.Y - y) < .5f && MathF.Abs(body.Z - (z + .5f)) < .5f;
    }

    /// <summary>
    /// GameLoop-local copy used only to restore an unexpected failed multi-drop commit. Normal
    /// planning prevents that path; the snapshot protects the all-or-nothing invariant if a
    /// store invariant changes between planning and commit.
    /// </summary>
    internal sealed class State
    {
        private readonly Dictionary<(int X, int Y, int Z), DropSlot> _drops;

        internal State(Dictionary<(int X, int Y, int Z), DropSlot> drops) => _drops = drops;

        internal Dictionary<(int X, int Y, int Z), DropSlot> Drops => _drops;
    }

    /// <summary>Result of a successful deposit (wire fan-out).</summary>
    public readonly record struct DepositResult(
        int X,
        int Y,
        int Z,
        StackId Id,
        int Count,
        long EntityRuntimeId,
        bool Created,
        bool CountChanged,
        FloorDropMotion? Motion = null);

    public FloorDropStore(ILogger? logger = null) => _logger = logger;

    public int Count => _drops.Count;

    internal State CaptureState() => new(new Dictionary<(int X, int Y, int Z), DropSlot>(_drops));

    internal void RestoreState(State snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _drops.Clear();
        foreach (var (key, slot) in snapshot.Drops)
            _drops.Add(key, slot);
    }

    /// <summary>
    /// Merge into an existing same-<see cref="StackId"/> cell, or place a new cell under SoftCap.
    /// </summary>
    public bool TryAddOrMerge(
        int x,
        int y,
        int z,
        StackId id,
        int count,
        long entityRuntimeIdIfNew,
        out DepositResult? deposit,
        int pickupDelayTicks = DefaultPickupDelay,
        FloorDropMotion? motion = null)
    {
        deposit = null;
        if (count <= 0 || id.IsEmpty) return true;
        var limit = ItemStackLimits.For(id);
        if (count > limit) return false;
        var key = (x, y, z);
        if (_drops.TryGetValue(key, out var existing))
        {
            // One StackId per cell — different id must use another cell (FloorDropFanout spiral).
            if (existing.Id != id) return false;
            if (motion.HasValue || !CanMergeAt(x, y, z)) return false;
            if (existing.Count > limit - count) return false;

            var merged = existing.Count + count;
            var delay = Math.Max(existing.PickupDelayTicks, pickupDelayTicks);
            // Age is not reset by topping off an existing pile — a cell doesn't get to live forever.
            _drops[key] = existing with { Count = merged, PickupDelayTicks = delay };
            deposit = new DepositResult(x, y, z, id, merged, existing.EntityRuntimeId, Created: false, CountChanged: merged != existing.Count, existing.Motion);
            return true;
        }

        if (_drops.Count >= SoftCap)
        {
            if (Interlocked.Exchange(ref _capWarned, 1) == 0)
            {
                _logger?.Warning(
                    $"FloorDropStore at SoftCap ({SoftCap}): refusing new floor-drop cells.");
            }

            return false;
        }

        var entityId = entityRuntimeIdIfNew;
        var newCount = count;
        _drops[key] = new DropSlot(id, newCount, entityId, Math.Max(0, pickupDelayTicks), Motion: motion);
        deposit = new DepositResult(
            x, y, z, id, newCount, entityId, Created: true, CountChanged: true, motion);
        return true;
    }

    /// <summary>Decrement pickup delays by <paramref name="tickDiff"/> (call once per GameLoop tick).</summary>
    public void TickPickupDelays(int tickDiff = 1)
    {
        if (tickDiff <= 0 || _drops.Count == 0) return;

        _delayScratch.Clear();
        foreach (var (key, slot) in _drops)
        {
            if (slot.PickupDelayTicks > 0)
                _delayScratch.Add(key);
        }

        for (var i = 0; i < _delayScratch.Count; i++)
        {
            var key = _delayScratch[i];
            if (!_drops.TryGetValue(key, out var slot) || slot.PickupDelayTicks <= 0)
                continue;
            var next = slot.PickupDelayTicks - tickDiff;
            if (next < 0) next = 0;
            _drops[key] = slot with { PickupDelayTicks = next };
        }
    }

    /// <summary>
    /// Ages every cell by <paramref name="tickDiff"/>; cells reaching <paramref name="despawnTicks"/>
    /// are removed and appended to <paramref name="expired"/> (caller sends RemoveActor / drops the wire entity).
    /// </summary>
    public void TickDespawn(
        List<(int X, int Y, int Z, StackId Id, int Count, long EntityRuntimeId)> expired,
        int tickDiff = 1,
        int despawnTicks = DefaultDespawnTicks)
    {
        if (tickDiff <= 0 || _drops.Count == 0) return;

        _ageScratch.Clear();
        foreach (var key in _drops.Keys)
            _ageScratch.Add(key);

        for (var i = 0; i < _ageScratch.Count; i++)
        {
            var key = _ageScratch[i];
            if (!_drops.TryGetValue(key, out var slot)) continue;

            var age = slot.AgeTicks + tickDiff;
            if (age >= despawnTicks)
            {
                _drops.Remove(key);
                expired.Add((key.X, key.Y, key.Z, slot.Id, slot.Count, slot.EntityRuntimeId));
            }
            else
            {
                _drops[key] = slot with { AgeTicks = age };
            }
        }
    }

    /// <summary>
    /// A real, eagerly-materialized copy — not a lazy iterator over <c>_drops</c>. This must stay
    /// eager: <see cref="FloorDropSystem"/>'s pickup loop mutates the store (via
    /// <see cref="TryTakeUpTo"/>) while iterating a previously-called <c>Snapshot()</c> result. A
    /// lazy <c>yield return</c> generator here throws "Collection was modified" the moment a second
    /// item is picked up in the same tick — found as a real bug (partial-pickup + an uncaught
    /// exception that permanently kills the GameLoop task, since <see cref="Zenith.Gameplay.Runtime.GameLoop.TickOnce"/>
    /// re-throws system failures) rather than a silent behavioral difference.
    /// </summary>
    public List<((int X, int Y, int Z) Pos, StackId Id, int Count, long EntityRuntimeId, int PickupDelayTicks, int AgeTicks)> Snapshot()
    {
        var result = new List<((int X, int Y, int Z), StackId, int, long, int, int)>(_drops.Count);
        foreach (var (pos, slot) in _drops)
            result.Add((pos, slot.Id, slot.Count, slot.EntityRuntimeId, slot.PickupDelayTicks, slot.AgeTicks));
        return result;
    }

    public bool TryTake(int x, int y, int z, out StackId id, out int count, out long entityRuntimeId)
    {
        if (!_drops.Remove((x, y, z), out var slot))
        {
            id = default;
            count = 0;
            entityRuntimeId = 0;
            return false;
        }

        id = slot.Id;
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
        out StackId id,
        out int taken,
        out long entityRuntimeId,
        out DepositResult? remainingPublish)
    {
        id = default;
        taken = 0;
        entityRuntimeId = 0;
        remainingPublish = null;

        if (max <= 0) return false;
        var key = (x, y, z);
        if (!_drops.TryGetValue(key, out var slot)) return false;

        id = slot.Id;
        entityRuntimeId = slot.EntityRuntimeId;
        taken = Math.Min(max, slot.Count);

        if (taken >= slot.Count)
        {
            _drops.Remove(key);
            return true;
        }

        var left = slot.Count - taken;
        _drops[key] = slot with { Count = left };
        remainingPublish = new DepositResult(
            x, y, z, slot.Id, left, slot.EntityRuntimeId,
            Created: false, CountChanged: true, slot.Motion);
        return true;
    }
}

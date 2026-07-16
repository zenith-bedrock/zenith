using System.Threading;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// Floor drops with optional Bedrock item-entity wire (ADR §26): sparse cells holding a stack
/// + entity runtime id, picked up when a player walks within 1.5 blocks on the GameLoop tick.
/// SoftCap refuses new cell keys (lossy vs entities — honest LAN bound).
/// </summary>
sealed class FloorDropStore
{
    public const int MaxStack = 64;
    internal const int SoftCap = 2048;

    private readonly Dictionary<(int X, int Y, int Z), DropSlot> _drops = new();
    private readonly ILogger? _logger;
    private int _capWarned;

    readonly record struct DropSlot(int RuntimeId, int Count, long EntityRuntimeId);

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
    /// Returns false when a new cell would exceed the soft cap (existing cells may still merge).
    /// </summary>
    public bool TryAddOrMerge(
        int x,
        int y,
        int z,
        int runtimeId,
        int count,
        long entityRuntimeIdIfNew,
        out DepositResult? deposit)
    {
        deposit = null;
        if (count <= 0 || runtimeId == Blocks.Air) return true;
        var key = (x, y, z);
        if (_drops.TryGetValue(key, out var existing) && existing.RuntimeId == runtimeId)
        {
            var merged = Math.Min(MaxStack, existing.Count + count);
            var changed = merged != existing.Count;
            _drops[key] = new DropSlot(runtimeId, merged, existing.EntityRuntimeId);
            deposit = new DepositResult(x, y, z, runtimeId, merged, existing.EntityRuntimeId, Created: false, CountChanged: changed);
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
        _drops[key] = new DropSlot(runtimeId, Math.Min(MaxStack, count), entityId);
        deposit = new DepositResult(
            x, y, z, runtimeId, Math.Min(MaxStack, count), entityId, Created: true, CountChanged: true);
        return true;
    }

    public IEnumerable<((int X, int Y, int Z) Pos, int RuntimeId, int Count, long EntityRuntimeId)> Snapshot()
    {
        foreach (var (pos, slot) in _drops)
            yield return (pos, slot.RuntimeId, slot.Count, slot.EntityRuntimeId);
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
}

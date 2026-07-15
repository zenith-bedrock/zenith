using System.Threading;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// Floor drops without Bedrock item entities (ADR §26 / §36): sparse cells holding a stack,
/// picked up when a player walks within 1.5 blocks on the GameLoop tick.
/// SoftCap refuses new cell keys (lossy vs entities — honest LAN bound).
/// </summary>
sealed class FloorDropStore
{
    public const int MaxStack = 64;
    internal const int SoftCap = 2048;

    private readonly Dictionary<(int X, int Y, int Z), DropSlot> _drops = new();
    private readonly ILogger? _logger;
    private int _capWarned;

    readonly record struct DropSlot(int RuntimeId, int Count);

    public FloorDropStore(ILogger? logger = null) => _logger = logger;

    public int Count => _drops.Count;

    /// <summary>Legacy alias — prefer <see cref="TryAddOrMerge"/> when refuse matters.</summary>
    public void AddOrMerge(int x, int y, int z, int runtimeId, int count) =>
        TryAddOrMerge(x, y, z, runtimeId, count);

    /// <summary>
    /// Merge into an existing same-item cell, or place a new cell under <see cref="SoftCap"/>.
    /// Returns false when a new cell would exceed the soft cap (existing cells may still merge).
    /// </summary>
    public bool TryAddOrMerge(int x, int y, int z, int runtimeId, int count)
    {
        if (count <= 0 || runtimeId == Blocks.Air) return true;
        var key = (x, y, z);
        if (_drops.TryGetValue(key, out var existing) && existing.RuntimeId == runtimeId)
        {
            var merged = Math.Min(MaxStack, existing.Count + count);
            _drops[key] = new DropSlot(runtimeId, merged);
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

        _drops[key] = new DropSlot(runtimeId, Math.Min(MaxStack, count));
        return true;
    }

    public IEnumerable<((int X, int Y, int Z) Pos, int RuntimeId, int Count)> Snapshot()
    {
        foreach (var (pos, slot) in _drops)
            yield return (pos, slot.RuntimeId, slot.Count);
    }

    public bool TryTake(int x, int y, int z, out int runtimeId, out int count)
    {
        if (!_drops.Remove((x, y, z), out var slot))
        {
            runtimeId = Blocks.Air;
            count = 0;
            return false;
        }

        runtimeId = slot.RuntimeId;
        count = slot.Count;
        return true;
    }
}

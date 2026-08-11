using System.Threading;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>One in-flight falling-block entity (ADR §95). RAM-only — never persisted.</summary>
sealed class FallingBlockEntry
{
    public required long EntityId { get; init; }
    public required ulong RuntimeId { get; init; }
    public required int BlockRuntimeId { get; init; }
    public required int LandX { get; init; }
    /// <summary>Destination cell Y — recomputed live each tick as the fall approaches ground.</summary>
    public required int LandY { get; set; }
    public required int LandZ { get; init; }
    /// <summary>Continuous vertical position — starts at the source block's Y, falls toward <see cref="LandY"/>.</summary>
    public float Y { get; set; }
    public float VelocityY { get; set; }
}

/// <summary>
/// Active falling-block entities (ADR §95) — replaces the instant column-teleport GravitySystem
/// used before with real, per-tick physics-driven entities the client actually sees fall.
/// SoftCap refuses new entries past the cap (same discipline as GravityPendingStore/FloorDropStore);
/// a fall in progress is naturally short-lived, so this is a safety net, not the primary throttle
/// (that's still <see cref="GravityPendingStore.MaxStepsPerTick"/> limiting new falls started per tick).
/// </summary>
sealed class FallingBlockStore
{
    internal const int SoftCap = 512;

    private readonly List<FallingBlockEntry> _active = new();
    private readonly ILogger? _logger;
    private int _capWarned;

    public FallingBlockStore(ILogger? logger = null) => _logger = logger;

    public IReadOnlyList<FallingBlockEntry> Active => _active;

    public bool TrySpawn(FallingBlockEntry entry)
    {
        if (_active.Count >= SoftCap)
        {
            if (Interlocked.Exchange(ref _capWarned, 1) == 0)
                _logger?.Warning($"FallingBlockStore at SoftCap ({SoftCap}): refusing new falling blocks.");
            return false;
        }

        _active.Add(entry);
        return true;
    }

    public void Remove(FallingBlockEntry entry) => _active.Remove(entry);
}

using System.Threading;
using Zenith.Logging;

namespace Zenith.World;

/// <summary>
/// One in-flight falling-block entity (ADR §95). The entity itself is RAM-only; what survives a restart is its
/// <see cref="Record"/>, saved inside the column image (plan 002, phase 4a), which gravity re-adopts when the column loads.
/// </summary>
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

    /// <summary>The saved record of this fall, in the column that holds both origin and landing. Removed only when the fall is resolved.</summary>
    public PendingFall? Record { get; init; }

    /// <summary>No cell below accepted the block yet; it is kept (never discarded) and retried periodically.</summary>
    public bool IsStuck { get; set; }

    public int RetryIn { get; set; }
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

    /// <summary>True while another entry can be tracked. Checked before a block is taken out of the world, so a full store never loses one.</summary>
    public bool HasRoom => _active.Count < SoftCap;

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

using System.Threading;
using Zenith.Logging;

namespace Zenith.World;

/// <summary>
/// One experience orb in the world (plan 003, phase A). RAM-only, like floor drops and falling blocks: an orb is a short-lived
/// pile of already-earned points, and a restart forgets the ones still on the ground.
/// </summary>
sealed class ExperienceOrbEntry
{
    public required long EntityId { get; init; }

    /// <summary>The experience points this orb gives, exactly once, to whoever collects it.</summary>
    public required int Value { get; set; }

    public required FloorDropMotion Pose { get; set; }

    public int AgeTicks { get; set; }

    /// <summary>The player it is homing on (runtime id), if any; looked for again about once a second.</summary>
    public long? TargetRuntimeId { get; set; }

    public int LookForTargetIn { get; set; }
}

/// <summary>
/// Live experience orbs. SoftCap refuses new entries (same discipline as the other entity stores); the caller then credits the
/// refused points straight to the killer, so a full store never loses experience.
/// </summary>
sealed class ExperienceOrbStore
{
    internal const int SoftCap = 256;

    private readonly List<ExperienceOrbEntry> _active = [];
    private readonly ILogger? _logger;
    private int _capWarned;

    public ExperienceOrbStore(ILogger? logger = null) => _logger = logger;

    public IReadOnlyList<ExperienceOrbEntry> Active => _active;

    public int Count => _active.Count;

    public bool TrySpawn(ExperienceOrbEntry entry)
    {
        if (_active.Count >= SoftCap)
        {
            if (Interlocked.Exchange(ref _capWarned, 1) == 0)
                _logger?.Warning($"ExperienceOrbStore at SoftCap ({SoftCap}): the points of further orbs are credited directly.");
            return false;
        }

        _active.Add(entry);
        return true;
    }

    public void Remove(ExperienceOrbEntry entry) => _active.Remove(entry);
}

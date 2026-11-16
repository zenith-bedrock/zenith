using System.Collections.Immutable;

namespace Zenith.Modules.Protect;

/// <summary>A named, axis-aligned box of protected cells; both corners are inclusive and stored ordered.</summary>
readonly record struct ProtectRegion(string Name, int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ)
{
    public static ProtectRegion Between(string name, int x1, int y1, int z1, int x2, int y2, int z2) => new(
        name, Math.Min(x1, x2), Math.Min(y1, y2), Math.Min(z1, z2), Math.Max(x1, x2), Math.Max(y1, y2), Math.Max(z1, z2));

    public bool Contains(int x, int y, int z) =>
        x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;
}

/// <summary>
/// The regions the module protects. Edited from a command (any thread) and read by the policies (game loop), so the set is an
/// immutable snapshot swapped under a short lock: a reader never sees a half-edited set and never blocks.
/// </summary>
sealed class ProtectRegions
{
    /// <summary>A bound so a runaway script or a typo cannot make every edit scan an unbounded list.</summary>
    public const int MaxRegions = 256;

    public const int MaxNameLength = 32;

    private readonly object _gate = new();
    private ImmutableArray<ProtectRegion> _regions = [];

    public ImmutableArray<ProtectRegion> Snapshot => _regions;

    public bool TryAdd(ProtectRegion region, out string error)
    {
        if (string.IsNullOrWhiteSpace(region.Name) || region.Name.Length > MaxNameLength || region.Name.Any(char.IsWhiteSpace))
        {
            error = $"A region name is 1 to {MaxNameLength} characters without spaces.";
            return false;
        }

        lock (_gate)
        {
            if (_regions.Length >= MaxRegions)
            {
                error = $"At most {MaxRegions} regions.";
                return false;
            }

            if (_regions.Any(existing => string.Equals(existing.Name, region.Name, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"A region named '{region.Name}' already exists.";
                return false;
            }

            _regions = _regions.Add(region);
        }

        error = "";
        return true;
    }

    public bool TryRemove(string name)
    {
        lock (_gate)
        {
            var kept = _regions.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (kept.Length == _regions.Length) return false;
            _regions = kept;
            return true;
        }
    }

    /// <summary>The first region covering the cell, if any.</summary>
    public bool TryFind(int x, int y, int z, out ProtectRegion region)
    {
        foreach (var candidate in _regions)
        {
            if (!candidate.Contains(x, y, z)) continue;
            region = candidate;
            return true;
        }

        region = default;
        return false;
    }
}

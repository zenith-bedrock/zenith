using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.RandomTicks;

/// <summary>
/// A leaf block the world grew (not one a player placed: those are persistent) vanishes once no trunk is within
/// <see cref="Reach"/> steps through leaves. Leaves drop nothing here (data: <c>drop.count 0</c>); saplings and apples
/// are not modelled yet. A neighbour in a chunk that is not resident is unknown, and unknown never decays a leaf.
/// </summary>
sealed class LeavesDecayBehavior : IRandomTickBehavior
{
    /// <summary>Steps from a trunk, through leaves, that still hold a leaf on.</summary>
    public const int Reach = 4;

    private readonly HashSet<(int, int, int)> _seen = [];
    private readonly List<(int X, int Y, int Z)> _frontier = [];
    private readonly List<(int X, int Y, int Z)> _next = [];

    public void OnRandomTick(RandomTickContext tick)
    {
        if (Blocks.Catalog.States.Bit(tick.BlockRuntimeId, StateKeys.Persistent)) return;
        if (Search(tick, out var unknown) || unknown) return;
        tick.TrySet(tick.X, tick.Y, tick.Z, World.World.AirRuntimeId);
    }

    /// <summary>Breadth-first through leaves, <see cref="Reach"/> steps at most; true as soon as a trunk is met.</summary>
    private bool Search(RandomTickContext tick, out bool unknown)
    {
        unknown = false;
        _seen.Clear();
        _frontier.Clear();
        _frontier.Add((tick.X, tick.Y, tick.Z));
        _seen.Add((tick.X, tick.Y, tick.Z));

        for (var step = 0; step < Reach; step++)
        {
            _next.Clear();
            foreach (var (x, y, z) in _frontier)
            {
                foreach (var (dx, dy, dz) in BlockFaces.Neighbours)
                {
                    var (nx, ny, nz) = (x + dx, y + dy, z + dz);
                    if (!_seen.Add((nx, ny, nz))) continue;
                    if (!tick.TryGetBlock(nx, ny, nz, out var id)) { unknown = true; continue; }
                    if (Blocks.IsLog(id)) return true;
                    if (Blocks.IsLeaves(id)) _next.Add((nx, ny, nz));
                }
            }

            _frontier.Clear();
            _frontier.AddRange(_next);
            if (_frontier.Count == 0) break;
        }

        return false;
    }
}

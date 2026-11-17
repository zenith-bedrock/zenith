using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// ADR §114/§167 — periodic residency sweep. Every <see cref="Server.ServerConfig.WorldConfig.ChunkResidencySweepIntervalTicks"/>
/// ticks it first queues the edited columns for the disk (a bounded number, so a busy world never stalls one tick), then drops
/// the columns nobody has in view from <see cref="World.World"/> (<see cref="World.World.TryEvictChunk"/>), which also saves
/// whatever was edited since.
/// </summary>
[GameSystem("chunk-residency")]
sealed class ChunkResidencySystem : IGameSystem
{
    /// <summary>Edited columns encoded per sweep; the rest wait for the next one (eviction and shutdown save any that are left).</summary>
    public const int MaxColumnsSavedPerSweep = 64;

    private readonly World.World _world;
    private readonly int _sweepIntervalTicks;
    private readonly List<(int X, int Z)> _candidatesScratch = new();

    public ChunkResidencySystem(World.World world, int sweepIntervalTicks)
    {
        _world = world;
        _sweepIntervalTicks = Math.Max(1, sweepIntervalTicks);
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick % (ulong)_sweepIntervalTicks != 0)
            return;

        _ = _world.PersistDirtyColumns(MaxColumnsSavedPerSweep);

        _candidatesScratch.Clear();
        _candidatesScratch.AddRange(_world.CopyLoadedColumns());
        foreach (var (x, z) in _candidatesScratch)
            _world.TryEvictChunk(x, z);

        _world.LogResidencySnapshot();
    }
}

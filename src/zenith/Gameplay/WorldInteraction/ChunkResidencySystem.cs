using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// ADR §114 — periodic eviction sweep. Every <see cref="Server.ServerConfig.WorldConfig.ChunkResidencySweepIntervalTicks"/>
/// ticks, drops overlays/chests for chunks nobody currently has in view from <see cref="World.World"/>'s
/// RAM side-tables (<see cref="World.World.TryEvictChunk"/>). This does not shrink ZLDB's own
/// always-resident dataset (ADR §11/§61 — non-goal to change); it removes the duplicate copy
/// <see cref="World.World"/>/<see cref="World.ChestStore"/> keep on top of it.
/// </summary>
sealed class ChunkResidencySystem : IGameSystem
{
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

        _candidatesScratch.Clear();
        _candidatesScratch.AddRange(_world.CopyHydratedChunks());
        foreach (var (x, z) in _candidatesScratch)
            _world.TryEvictChunk(x, z);

        _world.LogResidencySnapshot();
    }
}

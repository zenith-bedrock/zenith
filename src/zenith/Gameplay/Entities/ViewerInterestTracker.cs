namespace Zenith.Gameplay.Entities;

/// <summary>
/// Which players' interest in the world changed since the previous tick, so an actor's viewer set does not have to be re-derived against
/// every online player every tick.
///
/// Whether a player sees an actor is a function of the actor's chunk and of the player (in game, alive, the columns it knows, its view
/// radius). For an actor that did not change chunk, the only players whose answer can have changed are the ones whose own state changed,
/// and that is usually none or a handful, not all of them. <see cref="Begin"/> takes the per-tick snapshot and lists exactly those
/// players in <see cref="Changed"/>; a player seen for the first time counts as changed.
///
/// Contract: the snapshot is taken at the start of the species' tick. A state change made later in that same tick (a player dying to a
/// mob's attack) is picked up at the next <see cref="Begin"/>, one tick later, the same latency the world already has for everything
/// else that changes during a tick.
/// </summary>
sealed class ViewerInterestTracker
{
    private readonly record struct Snapshot(int ChunkVersion, int ViewRadius, bool InGameAndAlive, ulong Frame);

    private readonly Dictionary<long, Snapshot> _snapshots = [];
    private readonly List<Player.Player> _changed = [];
    private readonly List<long> _stale = [];

    /// <summary>Ticks begun so far; an actor reconciled in <c>Frame</c> or <c>Frame - 1</c> has seen every change before <see cref="Changed"/>.</summary>
    public ulong Frame { get; private set; }

    /// <summary>Players whose interest-relevant state differs from the previous <see cref="Begin"/> (or who are new).</summary>
    public IReadOnlyList<Player.Player> Changed => _changed;

    public void Begin(IReadOnlyList<Player.Player> online)
    {
        Frame++;
        _changed.Clear();
        foreach (var player in online)
        {
            var now = new Snapshot(player.Chunks.Version, player.Chunks.Radius, player.IsInGame && !player.IsDead, Frame);
            if (_snapshots.TryGetValue(player.RuntimeId, out var before) &&
                before.ChunkVersion == now.ChunkVersion && before.ViewRadius == now.ViewRadius && before.InGameAndAlive == now.InGameAndAlive)
            {
                _snapshots[player.RuntimeId] = before with { Frame = Frame };
                continue;
            }

            _snapshots[player.RuntimeId] = now;
            _changed.Add(player);
        }

        // Players that left are not in this frame's pass: forget them so the map does not grow with every join.
        if (_snapshots.Count == online.Count) return;
        _stale.Clear();
        foreach (var (runtimeId, snapshot) in _snapshots)
            if (snapshot.Frame != Frame) _stale.Add(runtimeId);
        foreach (var runtimeId in _stale) _snapshots.Remove(runtimeId);
    }
}

using System.Collections.Concurrent;

namespace Zenith.Player;

/// <summary>
/// Dono de todos os Players online. Ponto central pra achar player por nome e (quando fizer
/// sentido) fazer broadcast pra todo mundo. Uma instância por <see cref="Zenith.Server.ZenithServer"/>.
/// </summary>
class PlayerManager
{
    private readonly ConcurrentDictionary<string, Player> _players = new();
    // Connection teardown is a network-thread concern, but releasing a chest opener mutates
    // authoritative world container state. Each live Player can be queued once on disconnect.
    private readonly ConcurrentQueue<Player> _disconnectedContainerCleanup = new();
    private long _nextRuntimeId = 1;

    public int Count => _players.Count;

    /// <summary>
    /// Snapshot of who is online (allocates <c>ToArray</c>). Prefer <see cref="FillOnline"/> /
    /// <see cref="SnapshotOnline"/> — do not call this inside nested peer loops.
    /// </summary>
    public IReadOnlyList<Player> Online => _players.Values.ToArray();

    /// <summary>
    /// Snapshot online players into a new list (Session helpers / disconnect).
    /// Prefer tick-scoped <see cref="FillOnline"/> inside GameLoop systems.
    /// </summary>
    public List<Player> SnapshotOnline()
    {
        var list = new List<Player>(_players.Count);
        FillOnline(list);
        return list;
    }

    /// <summary>
    /// Clear <paramref name="buffer"/> and copy current online players into it (GameLoop once/tick).
    /// </summary>
    public void FillOnline(List<Player> buffer)
    {
        buffer.Clear();
        foreach (var player in _players.Values)
            buffer.Add(player);
    }

    /// <summary>Próximo runtime entity id estável (sem EntityManager).</summary>
    public long AllocateRuntimeId() => Interlocked.Increment(ref _nextRuntimeId);

    /// <summary>Retorna false se já existir um player online com o mesmo username.</summary>
    public bool TryAdd(Player player) => _players.TryAdd(player.Username, player);

    public void Remove(Player player) => _players.TryRemove(player.Username, out _);

    /// <summary>Network lifecycle handoff; consumed by <c>InventorySystem</c> on the GameLoop.</summary>
    public void SubmitDisconnectedContainerCleanup(Player player) =>
        _disconnectedContainerCleanup.Enqueue(player);

    /// <summary>GameLoop only — drains a disconnected player's pending chest opener release.</summary>
    public bool TryConsumeDisconnectedContainerCleanup(out Player player) =>
        _disconnectedContainerCleanup.TryDequeue(out player!);

    public Player? Get(string username) => _players.GetValueOrDefault(username);
}

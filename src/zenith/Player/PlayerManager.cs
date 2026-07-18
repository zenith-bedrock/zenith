using System.Collections.Concurrent;

namespace Zenith.Player;

/// <summary>
/// Dono de todos os Players online. Ponto central pra achar player por nome e (quando fizer
/// sentido) fazer broadcast pra todo mundo. Uma instância por <see cref="Zenith.Server.ZenithServer"/>.
/// </summary>
class PlayerManager
{
    private readonly ConcurrentDictionary<string, Player> _players = new();
    private long _nextRuntimeId = 1;

    public int Count => _players.Count;

    /// <summary>
    /// Snapshot of who is online (allocates). Prefer <see cref="FillOnline"/> once per tick
    /// and reuse that list; do not call this inside a nested peer loop.
    /// </summary>
    public IReadOnlyList<Player> Online => _players.Values.ToArray();

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

    public Player? Get(string username) => _players.GetValueOrDefault(username);
}

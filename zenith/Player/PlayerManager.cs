using System.Collections.Concurrent;

namespace Zenith.Player;

/// <summary>
/// Dono de todos os Players online. Ponto central pra achar player por nome e (quando fizer
/// sentido) fazer broadcast de pacote pra todo mundo. Uma instância por <see cref="Zenith.Server.ZenithServer"/>.
/// </summary>
class PlayerManager
{
    private readonly ConcurrentDictionary<string, Player> _players = new();

    public IReadOnlyCollection<Player> Online => _players.Values.ToArray();

    /// <summary>Retorna false se já existir um player online com o mesmo username.</summary>
    public bool TryAdd(Player player) => _players.TryAdd(player.Username, player);

    public void Remove(Player player) => _players.TryRemove(player.Username, out _);

    public Player? Get(string username) => _players.GetValueOrDefault(username);
}

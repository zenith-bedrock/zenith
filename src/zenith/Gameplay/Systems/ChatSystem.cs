using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Fan-out de chat no tick. Handler só enfileira; nunca escreve no RakNetSession dos peers.
/// </summary>
sealed class ChatSystem : IGameSystem
{
    private readonly PlayerManager _players;

    public ChatSystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock) => Tick(clock, _players.Online);

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            while (player.TryConsumeChat(out var message))
            {
                foreach (var peer in online)
                {
                    if (!peer.IsInGame) continue;
                    peer.Session.Protocol.Chat.SendChat(player.Username, message);
                }
            }
        }
    }
}

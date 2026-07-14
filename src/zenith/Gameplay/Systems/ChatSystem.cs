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

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            if (!player.TryConsumeChat(out var message)) continue;

            foreach (var peer in _players.Online)
            {
                if (!peer.IsInGame) continue;
                peer.Session.Protocol.Chat.SendChat(player.Username, message);
            }
        }
    }
}

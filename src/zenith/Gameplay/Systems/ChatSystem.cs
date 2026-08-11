using System.Collections.Generic;
using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Fan-out de chat no tick. Handler só enfileira; nunca escreve no RakNetSession dos peers.
/// </summary>
sealed class ChatSystem : IGameSystem
{
    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (!player.IsInGame) continue;

            while (player.TryConsumeChat(out var message))
            {
                foreach (var peer in online)
                {
                    if (!peer.IsInGame) continue;
                    peer.Session.Protocol.Chat.SendChat(
                        player.Username,
                        message,
                        player.Session.Profile.Xuid);
                }
            }
        }
    }
}

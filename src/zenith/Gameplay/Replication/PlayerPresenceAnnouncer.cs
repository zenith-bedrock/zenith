using Zenith.Event;
using Zenith.Server;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Join/leave system chat, driven by the world-presence facts (<see cref="PlayerEnteredWorld"/> / <see cref="PlayerLeftWorld"/>),
/// which exist only for players who actually spawned: a login that drops before spawning is never announced, either way.
/// Not a plugin surface: this is internal Gameplay composition, registered once at the
/// composition root (<see cref="ZenithServer"/>), same as any other system.
/// </summary>
static class PlayerPresenceAnnouncer
{
    public static void OnEntered(ServerContext context, PlayerEnteredWorld e) =>
        Broadcast(context, e.SessionId, $"§e{e.Username} joined the game");

    public static void OnLeft(ServerContext context, PlayerLeftWorld e) =>
        Broadcast(context, e.SessionId, $"§e{e.Username} left the game");

    /// <summary>Tells every in-game peer except the session the message is about.</summary>
    private static void Broadcast(ServerContext context, long subjectSessionId, string message)
    {
        foreach (var peer in context.PlayerManager.SnapshotOnline())
        {
            if (peer.RuntimeId == subjectSessionId || !peer.IsInGame) continue;
            peer.Client.Chat.SendSystem(message);
        }
    }
}

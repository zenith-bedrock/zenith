using Zenith.Event;
using Zenith.Server;

namespace Zenith.Gameplay;

/// <summary>
/// Join/leave system chat, driven by <see cref="EventBus"/> (ADR §78) — first real domain
/// consumers of <see cref="PlayerLoginEvent"/> / <see cref="PlayerQuitEvent"/>, proving the
/// seam holds for more than the zero consumers it shipped with (§21).
/// Not a plugin surface: this is internal Gameplay composition, registered once at the
/// composition root (<see cref="ZenithServer"/>), same as any other system.
/// </summary>
static class PlayerPresenceAnnouncer
{
    public static void OnLogin(ServerContext context, PlayerLoginEvent e) =>
        Broadcast(context, e.Player, $"§e{e.Player.Username} joined the game");

    public static void OnQuit(ServerContext context, PlayerQuitEvent e)
    {
        // Skip pre-spawn drops (login/resource-pack/spawn failures) — nobody saw them join.
        if (!e.WasInGame) return;
        Broadcast(context, e.Player, $"§e{e.Player.Username} left the game");
    }

    /// <summary>Peer loop always excludes <paramref name="subject"/> — production already
    /// removes/hasn't-yet-added them at these event points, but staying explicit here means
    /// this holds regardless of exactly when the caller mutated PlayerManager.</summary>
    private static void Broadcast(ServerContext context, Player.Player subject, string message)
    {
        foreach (var peer in context.PlayerManager.SnapshotOnline())
        {
            if (peer == subject || !peer.IsInGame) continue;
            peer.Session.Protocol.Chat.SendSystem(message);
        }
    }
}

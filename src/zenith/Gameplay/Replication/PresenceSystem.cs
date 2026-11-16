using Zenith.Event;
using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Turns departures the network thread queued into <see cref="PlayerLeftWorld"/> facts on the game loop. It runs after the systems
/// that finish a departed player's work (container release, inventory persistence), so the fact means the departure is complete.
/// </summary>
sealed class PresenceSystem(Player.PlayerManager players, EventBus events) : IGameSystem
{
    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        while (players.TryConsumeDeparture(out var departure))
        {
            events.Publish(new PlayerLeftWorld(
                departure.PlayerId, departure.Username, departure.SessionId, departure.Reason, clock.CurrentTick));
        }
    }
}

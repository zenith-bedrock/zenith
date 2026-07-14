using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Sincroniza o tempo do mundo com os clientes ~1×/s via Protocol.
/// </summary>
sealed class TimeSyncSystem : IGameSystem
{
    private readonly PlayerManager _players;

    public TimeSyncSystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock)
    {
        if (_players.Count == 0) return;
        if (clock.CurrentTick % GameClock.TicksPerSecond != 0) return;

        var time = clock.WorldTime;
        foreach (var player in _players.Online)
        {
            player.Session.Protocol.World.SendTime(time);
        }
    }
}

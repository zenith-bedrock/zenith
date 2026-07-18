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

    public void Tick(GameClock clock) => Tick(clock, _players.Online);

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (online.Count == 0) return;
        if (clock.CurrentTick % GameClock.TicksPerSecond != 0) return;

        var time = clock.WorldTime;
        foreach (var player in online)
        {
            player.Session.Protocol.World.SendTime(time);
        }
    }
}

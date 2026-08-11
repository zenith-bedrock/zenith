using System.Collections.Generic;
using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Sincroniza o tempo do mundo com os clientes ~1×/s via Protocol.
/// </summary>
sealed class TimeSyncSystem : IGameSystem
{
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

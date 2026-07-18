using Zenith.Player;

namespace Zenith.Gameplay.Runtime;

/// <summary>
/// Contrato mínimo: o GameLoop só chama Tick — sem conhecer a lógica interna do sistema.
/// <paramref name="online"/> is the tick-scoped snapshot (filled once per tick).
/// </summary>
interface IGameSystem
{
    void Tick(GameClock clock, IReadOnlyList<Player.Player> online);
}

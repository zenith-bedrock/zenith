namespace Zenith.Gameplay.Runtime;

/// <summary>
/// Contrato mínimo: o GameLoop só chama Tick — sem conhecer a lógica interna do sistema.
/// </summary>
interface IGameSystem
{
    void Tick(GameClock clock);
}

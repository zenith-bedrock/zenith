using Zenith.World;

namespace Zenith.Ecs;

/// <summary>
/// Villager's feature-specific state: the one trade it offers (<see cref="Ware"/> for <see cref="RequestedWare"/>), when the next
/// trade may complete, and its wander heading. Position, health, fall speed and despawn tracking are the shared components.
/// </summary>
struct VillagerState
{
    public StackId Ware;
    public StackId RequestedWare;
    public ulong NextTradeTick;
    public float WanderDirectionX;
    public float WanderDirectionZ;
    public ulong WanderChangeAtTick;
}

namespace Zenith.Ecs;

/// <summary>Bat's feature-specific state: a 3D wander heading and when to pick the next one. Same shape as <see cref="FishState"/>; the difference is the flight rule in <c>BatSystem.TryFly</c>.</summary>
struct BatState
{
    public float WanderDirectionX;
    public float WanderDirectionY;
    public float WanderDirectionZ;
    public ulong WanderChangeAtTick;
}

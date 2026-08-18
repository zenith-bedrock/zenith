namespace Zenith.Ecs;

/// <summary>
/// Fish's feature-specific state: 3D wander heading, structurally identical to what a flight-model
/// mob would need — the difference from Bat is entirely in <c>FishSystem.TrySwim</c>'s validity
/// rule, not in how a heading is picked or stored.
/// </summary>
struct FishState
{
    public float WanderDirectionX;
    public float WanderDirectionY;
    public float WanderDirectionZ;
    public ulong WanderChangeAtTick;
}

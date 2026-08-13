namespace Zenith.Ecs;

/// <summary>
/// Phase XXII — Cow's feature-specific state: wander heading/timer and breed cooldown. Not shared
/// with any other migrated species (Zombie/Minecart/Projectile's feature components are similarly
/// each owned by exactly one system). <c>LastSeenNearPlayerTick</c> is NOT here — it moved to the
/// shared <see cref="DespawnTracking"/> component, same as every other despawn-eligible actor.
/// </summary>
struct CowState
{
    public float WanderDirectionX;
    public float WanderDirectionZ;
    public ulong WanderChangeAtTick;
    public ulong BreedCooldownUntilTick;
}

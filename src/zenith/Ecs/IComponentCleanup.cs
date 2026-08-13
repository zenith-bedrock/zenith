namespace Zenith.Ecs;

/// <summary>
/// Low-level structural-teardown seam <see cref="EntityWorld"/> uses to remove every attached
/// component exactly once on <see cref="EntityWorld.Destroy"/>, without knowing what any
/// component means. Every <see cref="ComponentStore{T}"/> registers itself with its owning
/// <see cref="EntityWorld"/> at construction; nothing else implements this. Not reflection-driven:
/// registration is one explicit call per store, known at compile time.
/// </summary>
interface IComponentCleanup
{
    /// <summary>No-op if the entity never had this component. Never throws for a missing component.</summary>
    void RemoveIfPresent(EntityId id);
}

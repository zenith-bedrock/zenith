namespace Zenith.Ecs;

/// <summary>
/// Phase XXI — internal ECS identity. Deliberately NOT the same identity as Bedrock's
/// <c>ActorRuntimeId</c>/<c>ActorUniqueId</c> (see <see cref="RuntimeIdIndex"/> for the explicit
/// mapping between the two) — this type never leaves the runtime/gameplay boundary, is never
/// serialized, and carries no protocol meaning.
///
/// Generation-safe: <see cref="Index"/> names a slot that gets reused after
/// <see cref="EntityWorld.Destroy"/>; <see cref="Generation"/> increments on reuse so a handle
/// captured before a destroy can never silently address whatever was allocated into that slot
/// afterward. <see cref="EntityWorld.IsAlive"/> is the only authority for whether an
/// <see cref="EntityId"/> is currently valid.
/// </summary>
readonly record struct EntityId(int Index, int Generation)
{
    public static readonly EntityId Invalid = new(-1, 0);

    public bool IsValid => Index >= 0;
}

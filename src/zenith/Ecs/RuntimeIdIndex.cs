namespace Zenith.Ecs;

/// <summary>
/// Phase XXI — the explicit, one-way mapping from a Bedrock <c>ActorRuntimeId</c> to the internal
/// <see cref="EntityId"/> that owns it. Keyed by <c>ulong</c> deliberately — the same type
/// <see cref="ActorIdentity.ActorRuntimeId"/> already uses — so registering the wrong identifier
/// (e.g. <see cref="ActorIdentity.ActorUniqueId"/>, a <c>long</c>) is a compile error, not a
/// silent semantic mismatch. Phase XXI addendum: an earlier draft of
/// <see cref="EntityRuntime.CreateActor"/> registered the unique id here instead, which happened
/// to work only because every current actor allocates both ids from the same numeric source —
/// see docs/history/phases/phase-xxi-ecs-foundation-findings.md for why that was a landmine, not a bug that ever
/// fired.
///
/// Deliberately separate from <see cref="EntityWorld"/>: EntityWorld never reads a Bedrock id, and
/// this index never allocates or destroys an <see cref="EntityId"/> — it only tracks the
/// association. Owned by runtime/gameplay infrastructure (composed into <c>ZenithServer</c>
/// alongside <see cref="EntityWorld"/>), never by Protocol.
/// </summary>
sealed class RuntimeIdIndex
{
    private readonly Dictionary<ulong, EntityId> _byRuntimeId = new();

    /// <summary>Registers the mapping. Returns false (and registers nothing) if the runtime id is already mapped to a different, still-tracked entity — duplicate runtime ids are rejected, not silently overwritten.</summary>
    public bool Register(ulong actorRuntimeId, EntityId id)
    {
        if (_byRuntimeId.ContainsKey(actorRuntimeId)) return false;
        _byRuntimeId[actorRuntimeId] = id;
        return true;
    }

    public bool TryResolve(ulong actorRuntimeId, out EntityId id) => _byRuntimeId.TryGetValue(actorRuntimeId, out id);

    /// <summary>No-op if the runtime id isn't mapped — same "safe to call unconditionally" shape as <see cref="EntityWorld.Destroy"/>.</summary>
    public void Unregister(ulong actorRuntimeId) => _byRuntimeId.Remove(actorRuntimeId);

    internal int Count => _byRuntimeId.Count;
}

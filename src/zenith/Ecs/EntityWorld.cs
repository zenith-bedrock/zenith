namespace Zenith.Ecs;

/// <summary>
/// Phase XXI — the single owner of ECS entity identity: the generation-safe index allocator and
/// the registry of <see cref="ComponentStore{T}"/> instances (for structural teardown only, never
/// for gameplay meaning). Single-writer, same as every other piece of GameLoop-owned state —
/// nothing here is thread-safe, and nothing needs to be: <see cref="GameLoop"/> remains the sole
/// tick authority.
///
/// It does NOT own: packet encoding, AI decisions, player sessions, RakNet, persistence policy,
/// or Bedrock runtime-id mapping (see <see cref="RuntimeIdIndex"/>, a separate small class —
/// EntityWorld never reads a Bedrock id). Component *stores* register here for cleanup;
/// component *meaning* is entirely gameplay's concern.
/// </summary>
sealed class EntityWorld
{
    private int[] _generations = new int[64];
    private bool[] _alive = new bool[64];
    private readonly Stack<int> _freeIndices = new();
    private int _nextIndex;

    private readonly List<IComponentCleanup> _componentStores = new();

    internal long CreatedCount { get; private set; }
    internal long DestroyedCount { get; private set; }
    internal int AliveCount => (int)(CreatedCount - DestroyedCount);

    /// <summary>Allocates a fresh <see cref="EntityId"/> — a reused slot's generation is always one higher than its last occupant's.</summary>
    public EntityId Create()
    {
        int index;
        if (_freeIndices.TryPop(out var freed))
        {
            index = freed;
        }
        else
        {
            index = _nextIndex++;
            EnsureCapacity(index + 1);
        }

        _alive[index] = true;
        CreatedCount++;
        return new EntityId(index, _generations[index]);
    }

    public bool IsAlive(EntityId id) =>
        id.IsValid && id.Index < _nextIndex && _alive[id.Index] && _generations[id.Index] == id.Generation;

    /// <summary>
    /// Removes every registered component (each store's own no-op-if-absent
    /// <see cref="IComponentCleanup.RemoveIfPresent"/>), frees the slot for reuse, and bumps its
    /// generation so any stale handle a caller still holds fails <see cref="IsAlive"/> forever.
    /// Double-destroy is a safe no-op — the second call finds <see cref="IsAlive"/> already false
    /// and does nothing. Callers that also maintain a <see cref="RuntimeIdIndex"/> entry must
    /// unmap it themselves before or after calling this — <c>EntityWorld</c> has no Bedrock id to
    /// unmap with.
    /// </summary>
    public void Destroy(EntityId id)
    {
        if (!IsAlive(id)) return;

        foreach (var store in _componentStores)
            store.RemoveIfPresent(id);

        _alive[id.Index] = false;
        _generations[id.Index]++;
        _freeIndices.Push(id.Index);
        DestroyedCount++;
    }

    /// <summary>Structural-teardown registration — called once per <see cref="ComponentStore{T}"/> at construction, never per-entity.</summary>
    internal void RegisterStore(IComponentCleanup store) => _componentStores.Add(store);

    private void EnsureCapacity(int required)
    {
        if (required <= _generations.Length) return;
        var newSize = Math.Max(required, _generations.Length * 2);
        Array.Resize(ref _generations, newSize);
        Array.Resize(ref _alive, newSize);
    }
}

namespace Zenith.Ecs;

/// <summary>
/// Phase XXI — typed, sparse-set component storage for one component type. Optimized for value
/// types: no boxing, no reflection, no per-component allocation once the backing arrays have
/// grown to fit. Dense arrays keep iteration (<see cref="Entities"/>/<see cref="Values"/>)
/// contiguous; the sparse array gives O(1) <see cref="Has"/>/<see cref="TryGet"/>/<see cref="Set"/>
/// by <see cref="EntityId"/> without a <c>Dictionary&lt;EntityId,T&gt;</c>'s per-entry allocation.
///
/// Every read/write validates the entity is still alive in the owning <see cref="EntityWorld"/> —
/// a stale <see cref="EntityId"/> (destroyed, or a reused slot's old generation) is silently
/// rejected everywhere rather than corrupting whatever now occupies that slot.
///
/// Deliberately not archetype storage: components of different types for the same entity live in
/// entirely separate <see cref="ComponentStore{T}"/> instances with no shared row layout. See
/// docs/ecs.md for why that trade was made and what would justify revisiting it.
/// </summary>
sealed class ComponentStore<T> : IComponentCleanup where T : struct
{
    private readonly EntityWorld _world;
    private int[] _sparse = new int[64]; // EntityId.Index -> dense slot, or -1
    private readonly List<EntityId> _denseEntities = new();
    private readonly List<T> _denseValues = new();

    public ComponentStore(EntityWorld world)
    {
        _world = world;
        Array.Fill(_sparse, -1);
        world.RegisterStore(this);
    }

    public int Count => _denseEntities.Count;
    internal IReadOnlyList<EntityId> Entities => _denseEntities;

    public bool Has(EntityId id) =>
        _world.IsAlive(id) && id.Index < _sparse.Length && _sparse[id.Index] != -1;

    public bool TryGet(EntityId id, out T value)
    {
        if (!Has(id))
        {
            value = default;
            return false;
        }

        value = _denseValues[_sparse[id.Index]];
        return true;
    }

    /// <summary>Direct mutable access — throws if the entity doesn't have this component or is no longer alive; call <see cref="Has"/> first when that's a real possibility.</summary>
    public ref T GetRef(EntityId id)
    {
        if (!Has(id))
            throw new InvalidOperationException($"{typeof(T).Name} not present (or entity not alive) for {id}.");

        var denseIndex = _sparse[id.Index];
        return ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_denseValues)[denseIndex];
    }

    /// <summary>Adds the component if absent, or overwrites it if already present. No-op (returns false) for a stale/dead entity.</summary>
    public bool Set(EntityId id, in T value)
    {
        if (!_world.IsAlive(id)) return false;

        EnsureSparseCapacity(id.Index);
        var denseIndex = _sparse[id.Index];
        if (denseIndex != -1)
        {
            _denseValues[denseIndex] = value;
            return true;
        }

        _sparse[id.Index] = _denseEntities.Count;
        _denseEntities.Add(id);
        _denseValues.Add(value);
        return true;
    }

    /// <summary>
    /// Swap-remove from the dense arrays — O(1), reorders iteration order (documented: no
    /// ordering guarantee across removals). Validates generation like every other operation here:
    /// a stale <see cref="EntityId"/> whose slot has already been reused by a newer generation
    /// must never remove the new occupant's component. This check runs even when called from
    /// <see cref="EntityWorld.Destroy"/>'s structural teardown — at that point in the destroy
    /// sequence the entity being destroyed is still reported alive, so legitimate cleanup is
    /// unaffected; only a stale caller-held handle is rejected.
    /// </summary>
    public bool Remove(EntityId id)
    {
        if (!_world.IsAlive(id)) return false;
        if (id.Index < 0 || id.Index >= _sparse.Length) return false;
        var denseIndex = _sparse[id.Index];
        if (denseIndex == -1) return false;

        var lastIndex = _denseEntities.Count - 1;
        var lastEntity = _denseEntities[lastIndex];
        _denseEntities[denseIndex] = lastEntity;
        _denseValues[denseIndex] = _denseValues[lastIndex];
        _denseEntities.RemoveAt(lastIndex);
        _denseValues.RemoveAt(lastIndex);
        _sparse[lastEntity.Index] = denseIndex;
        _sparse[id.Index] = -1;
        return true;
    }

    void IComponentCleanup.RemoveIfPresent(EntityId id) => Remove(id);

    private void EnsureSparseCapacity(int index)
    {
        if (index < _sparse.Length) return;
        var newSize = Math.Max(index + 1, _sparse.Length * 2);
        var old = _sparse.Length;
        Array.Resize(ref _sparse, newSize);
        for (var i = old; i < newSize; i++) _sparse[i] = -1;
    }
}

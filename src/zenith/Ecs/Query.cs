namespace Zenith.Ecs;

/// <summary>
/// Phase XXI — allocation-free, deterministic multi-component queries. No LINQ, no expression
/// trees, no runtime query compiler: each arity is a small hand-written struct enumerator.
///
/// <b>Driver semantics (explicit, not automatic — addendum-clarified):</b> the FIRST type
/// parameter's store is always the one iterated; every other type is a pure <see
/// cref="ComponentStore{T}.Has"/> filter. <c>Query.With(positions, health)</c> walks
/// <c>positions</c>'s dense array and filters by <c>health.Has(...)</c> — it does NOT inspect
/// store sizes and pick whichever happens to be smaller. This is a deliberate DX choice: the
/// caller controls query cost by ordering arguments (put the smaller/rarer component first), and
/// that cost is visible at the call site rather than hidden behind a runtime heuristic. Get this
/// wrong (driving on the largest store) and the query still returns correct results — just not
/// the cheapest possible iteration.
///
/// Mutation rule: do not add/remove a component of a queried type from the entity currently being
/// visited mid-iteration (the same rule as mutating a <c>List&lt;T&gt;</c> while foreach-ing it —
/// the dense array a query is walking is exactly a <c>List&lt;T&gt;</c>). Mutating a *different*
/// entity's components, or mutating non-queried components on the current entity, is safe.
/// Structural changes are applied immediately — there is no deferred command buffer in this
/// first ECS (see docs/ecs.md, "Structural mutation rules").
/// </summary>
static class Query
{
    public static Query2<T1, T2> With<T1, T2>(ComponentStore<T1> a, ComponentStore<T2> b)
        where T1 : struct where T2 : struct => new(a, b);

    public static Query3<T1, T2, T3> With<T1, T2, T3>(ComponentStore<T1> a, ComponentStore<T2> b, ComponentStore<T3> c)
        where T1 : struct where T2 : struct where T3 : struct => new(a, b, c);
}

readonly struct Query2<T1, T2> where T1 : struct where T2 : struct
{
    private readonly ComponentStore<T1> _a;
    private readonly ComponentStore<T2> _b;

    internal Query2(ComponentStore<T1> a, ComponentStore<T2> b)
    {
        _a = a;
        _b = b;
    }

    public Enumerator GetEnumerator() => new(_a, _b);

    public struct Enumerator
    {
        private readonly IReadOnlyList<EntityId> _drivingEntities;
        private readonly ComponentStore<T2> _filterB;
        private int _index;

        internal Enumerator(ComponentStore<T1> a, ComponentStore<T2> b)
        {
            _drivingEntities = a.Entities;
            _filterB = b;
            _index = -1;
        }

        public EntityId Current => _drivingEntities[_index];

        public bool MoveNext()
        {
            while (true)
            {
                _index++;
                if (_index >= _drivingEntities.Count) return false;
                if (_filterB.Has(_drivingEntities[_index])) return true;
            }
        }
    }
}

readonly struct Query3<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct
{
    private readonly ComponentStore<T1> _a;
    private readonly ComponentStore<T2> _b;
    private readonly ComponentStore<T3> _c;

    internal Query3(ComponentStore<T1> a, ComponentStore<T2> b, ComponentStore<T3> c)
    {
        _a = a;
        _b = b;
        _c = c;
    }

    public Enumerator GetEnumerator() => new(_a, _b, _c);

    public struct Enumerator
    {
        private readonly IReadOnlyList<EntityId> _entities;
        private readonly ComponentStore<T2> _filterB;
        private readonly ComponentStore<T3> _filterC;
        private int _index;

        internal Enumerator(ComponentStore<T1> a, ComponentStore<T2> b, ComponentStore<T3> c)
        {
            _entities = a.Entities;
            _filterB = b;
            _filterC = c;
            _index = -1;
        }

        public EntityId Current => _entities[_index];

        public bool MoveNext()
        {
            while (true)
            {
                _index++;
                if (_index >= _entities.Count) return false;
                var candidate = _entities[_index];
                if (_filterB.Has(candidate) && _filterC.Has(candidate)) return true;
            }
        }
    }
}

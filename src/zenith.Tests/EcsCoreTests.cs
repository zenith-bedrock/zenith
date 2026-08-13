using Zenith.Ecs;
using Xunit;

namespace Zenith.Tests;

/// <summary>Phase XXI — leaf tests for the ECS entity allocator (generation safety) and component storage.</summary>
public sealed class EntityWorldTests
{
    [Fact]
    public void A_freshly_created_entity_is_alive()
    {
        var world = new EntityWorld();
        var id = world.Create();
        Assert.True(world.IsAlive(id));
    }

    [Fact]
    public void Destroy_invalidates_the_entity()
    {
        var world = new EntityWorld();
        var id = world.Create();

        world.Destroy(id);

        Assert.False(world.IsAlive(id));
    }

    [Fact]
    public void A_reused_slot_has_a_different_generation()
    {
        var world = new EntityWorld();
        var first = world.Create();
        world.Destroy(first);

        var second = world.Create();

        Assert.Equal(first.Index, second.Index);
        Assert.NotEqual(first.Generation, second.Generation);
    }

    [Fact]
    public void A_stale_handle_cannot_observe_the_new_occupant_of_its_reused_slot()
    {
        var world = new EntityWorld();
        var first = world.Create();
        world.Destroy(first);
        var second = world.Create();

        Assert.False(world.IsAlive(first));
        Assert.True(world.IsAlive(second));
    }

    [Fact]
    public void Double_destroy_is_a_safe_no_op()
    {
        var world = new EntityWorld();
        var id = world.Create();

        world.Destroy(id);
        world.Destroy(id); // must not throw or corrupt allocator state

        Assert.False(world.IsAlive(id));
        var next = world.Create();
        Assert.True(world.IsAlive(next));
    }

    [Fact]
    public void An_invalid_id_is_never_reported_alive()
    {
        var world = new EntityWorld();
        Assert.False(world.IsAlive(EntityId.Invalid));
        Assert.False(world.IsAlive(new EntityId(999, 0))); // never allocated
    }
}

public sealed class ComponentStoreTests
{
    private readonly record struct Vec2(float X, float Y);

    [Fact]
    public void Set_then_TryGet_round_trips_the_value()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var id = world.Create();

        Assert.True(store.Set(id, new Vec2(1, 2)));

        Assert.True(store.TryGet(id, out var value));
        Assert.Equal(new Vec2(1, 2), value);
        Assert.True(store.Has(id));
    }

    [Fact]
    public void Set_again_overwrites_rather_than_duplicating()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var id = world.Create();
        store.Set(id, new Vec2(1, 1));

        store.Set(id, new Vec2(9, 9));

        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet(id, out var value));
        Assert.Equal(new Vec2(9, 9), value);
    }

    [Fact]
    public void GetRef_allows_in_place_mutation()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var id = world.Create();
        store.Set(id, new Vec2(1, 1));

        store.GetRef(id) = new Vec2(5, 5);

        Assert.True(store.TryGet(id, out var value));
        Assert.Equal(new Vec2(5, 5), value);
    }

    [Fact]
    public void Remove_clears_the_component_without_affecting_others()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var a = world.Create();
        var b = world.Create();
        store.Set(a, new Vec2(1, 1));
        store.Set(b, new Vec2(2, 2));

        Assert.True(store.Remove(a));

        Assert.False(store.Has(a));
        Assert.True(store.Has(b));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Destroying_the_entity_removes_the_component_exactly_once_without_manual_bookkeeping()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var id = world.Create();
        store.Set(id, new Vec2(1, 1));

        world.Destroy(id);

        Assert.False(store.Has(id));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void A_stale_id_cannot_read_or_mutate_a_component_after_its_slot_is_reused()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var first = world.Create();
        store.Set(first, new Vec2(1, 1));
        world.Destroy(first);
        var second = world.Create();
        store.Set(second, new Vec2(2, 2));

        Assert.False(store.Has(first));
        Assert.False(store.TryGet(first, out _));
        Assert.False(store.Set(first, new Vec2(9, 9))); // stale write rejected
        Assert.True(store.TryGet(second, out var secondValue));
        Assert.Equal(new Vec2(2, 2), secondValue); // unaffected by the stale write attempt
    }

    /// <summary>
    /// Phase XXI addendum — the exact scenario flagged as under-tested: a stale handle whose slot
    /// index has been reused by a newer generation must never remove (or otherwise affect) the new
    /// occupant's component, not just be rejected by Has/TryGet/Set.
    /// </summary>
    [Fact]
    public void Remove_with_a_stale_generation_cannot_remove_the_new_occupants_component()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var a = world.Create();
        store.Set(a, new Vec2(1, 1));
        world.Destroy(a);

        var b = world.Create();
        Assert.Equal(a.Index, b.Index); // slot reuse — the case this test exists to cover
        store.Set(b, new Vec2(2, 2));

        var removed = store.Remove(a); // stale generation at a reused index

        Assert.False(removed);
        Assert.True(store.Has(b));
        Assert.True(store.TryGet(b, out var value));
        Assert.Equal(new Vec2(2, 2), value);
    }

    [Fact]
    public void GetRef_throws_rather_than_exposing_a_stale_handles_reused_slot()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var a = world.Create();
        store.Set(a, new Vec2(1, 1));
        world.Destroy(a);
        var b = world.Create();
        store.Set(b, new Vec2(2, 2));

        Assert.Throws<InvalidOperationException>(() => store.GetRef(a));
    }

    [Fact]
    public void Has_with_a_stale_generation_at_a_reused_index_reports_false()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var a = world.Create();
        store.Set(a, new Vec2(1, 1));
        world.Destroy(a);
        var b = world.Create();
        store.Set(b, new Vec2(2, 2));

        Assert.False(store.Has(a));
        Assert.True(store.Has(b));
    }

    [Fact]
    public void Set_on_a_dead_entity_is_rejected()
    {
        var world = new EntityWorld();
        var store = new ComponentStore<Vec2>(world);
        var id = world.Create();
        world.Destroy(id);

        Assert.False(store.Set(id, new Vec2(1, 1)));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Multiple_component_types_on_the_same_entity_are_independently_cleaned_up()
    {
        var world = new EntityWorld();
        var positions = new ComponentStore<Vec2>(world);
        var tags = new ComponentStore<int>(world);
        var id = world.Create();
        positions.Set(id, new Vec2(1, 1));
        tags.Set(id, 42);

        world.Destroy(id);

        Assert.False(positions.Has(id));
        Assert.False(tags.Has(id));
    }
}

public sealed class QueryTests
{
    private readonly record struct A(int Value);
    private readonly record struct B(int Value);
    private readonly record struct C(int Value);

    [Fact]
    public void Query2_yields_only_entities_with_both_components()
    {
        var world = new EntityWorld();
        var a = new ComponentStore<A>(world);
        var b = new ComponentStore<B>(world);
        var both = world.Create();
        var onlyA = world.Create();
        a.Set(both, new A(1));
        b.Set(both, new B(1));
        a.Set(onlyA, new A(2));

        var results = new List<EntityId>();
        foreach (var id in Query.With(a, b)) results.Add(id);

        Assert.Equal([both], results);
    }

    [Fact]
    public void Query3_yields_only_entities_with_all_three_components()
    {
        var world = new EntityWorld();
        var a = new ComponentStore<A>(world);
        var b = new ComponentStore<B>(world);
        var c = new ComponentStore<C>(world);
        var all = world.Create();
        var missingC = world.Create();
        a.Set(all, new A(1));
        b.Set(all, new B(1));
        c.Set(all, new C(1));
        a.Set(missingC, new A(2));
        b.Set(missingC, new B(2));

        var results = new List<EntityId>();
        foreach (var id in Query.With(a, b, c)) results.Add(id);

        Assert.Equal([all], results);
    }

    [Fact]
    public void Query_reflects_removal_between_iterations()
    {
        var world = new EntityWorld();
        var a = new ComponentStore<A>(world);
        var b = new ComponentStore<B>(world);
        var id = world.Create();
        a.Set(id, new A(1));
        b.Set(id, new B(1));

        Assert.Single(Collect(Query.With(a, b)));

        b.Remove(id);

        Assert.Empty(Collect(Query.With(a, b)));
    }

    [Fact]
    public void Query_reflects_destruction_between_iterations()
    {
        var world = new EntityWorld();
        var a = new ComponentStore<A>(world);
        var b = new ComponentStore<B>(world);
        var id = world.Create();
        a.Set(id, new A(1));
        b.Set(id, new B(1));

        Assert.Single(Collect(Query.With(a, b)));

        world.Destroy(id);

        Assert.Empty(Collect(Query.With(a, b)));
    }

    private static List<EntityId> Collect(Query2<A, B> query)
    {
        var results = new List<EntityId>();
        foreach (var id in query) results.Add(id);
        return results;
    }
}

public sealed class RuntimeIdIndexTests
{
    [Fact]
    public void Register_then_resolve_round_trips()
    {
        var world = new EntityWorld();
        var index = new RuntimeIdIndex();
        var id = world.Create();

        Assert.True(index.Register(100, id));

        Assert.True(index.TryResolve(100, out var resolved));
        Assert.Equal(id, resolved);
    }

    [Fact]
    public void Unresolved_runtime_id_is_rejected()
    {
        var index = new RuntimeIdIndex();
        Assert.False(index.TryResolve(999, out _));
    }

    [Fact]
    public void Duplicate_runtime_id_registration_is_rejected_deterministically()
    {
        var world = new EntityWorld();
        var index = new RuntimeIdIndex();
        var first = world.Create();
        var second = world.Create();
        Assert.True(index.Register(1, first));

        Assert.False(index.Register(1, second));

        Assert.True(index.TryResolve(1, out var resolved));
        Assert.Equal(first, resolved); // the original mapping is unchanged, not silently overwritten
    }

    [Fact]
    public void Destroy_then_unregister_leaves_no_stale_mapping()
    {
        var world = new EntityWorld();
        var index = new RuntimeIdIndex();
        var id = world.Create();
        index.Register(1, id);

        world.Destroy(id);
        index.Unregister(1);

        Assert.False(index.TryResolve(1, out _));
    }

    [Fact]
    public void Unregistering_an_unmapped_runtime_id_is_a_safe_no_op()
    {
        var index = new RuntimeIdIndex();
        index.Unregister(12345); // never registered
        Assert.Equal(0, index.Count);
    }
}

/// <summary>Phase XXI addendum 2 — leaf tests for <see cref="EntityRuntime"/>'s composition commands.</summary>
public sealed class EntityRuntimeTests
{
    [Fact]
    public void CreateActor_registers_by_runtime_id_not_unique_id()
    {
        var runtime = new EntityRuntime();

        // Deliberately different values — every migrated system today allocates them equal, but
        // this proves RuntimeIdIndex is keyed on the semantic identifier it claims to index, not
        // on that coincidence.
        var id = runtime.CreateActor(actorUniqueId: 1, actorRuntimeId: 999, 0, 0, 0);

        Assert.NotNull(id);
        Assert.True(runtime.RuntimeIds.TryResolve(999, out var resolved));
        Assert.Equal(id, resolved);
        Assert.False(runtime.RuntimeIds.TryResolve(1, out _)); // the unique id was never registered as a runtime id
    }

    [Fact]
    public void CreateActor_is_transactional_a_duplicate_runtime_id_leaves_nothing_behind()
    {
        var runtime = new EntityRuntime();
        var a = runtime.CreateActor(actorUniqueId: 1, actorRuntimeId: 100, 1, 2, 3);
        Assert.NotNull(a);

        var b = runtime.CreateActor(actorUniqueId: 2, actorRuntimeId: 100, 9, 9, 9); // same runtime id

        Assert.Null(b);
        // A is untouched.
        Assert.True(runtime.Entities.IsAlive(a!.Value));
        Assert.True(runtime.RuntimeIds.TryResolve(100, out var resolved));
        Assert.Equal(a.Value, resolved);
        Assert.True(runtime.Positions.TryGet(a.Value, out var pos));
        Assert.Equal(1f, pos.X);
        // B never persisted: no live entity, no leaked components, runtime id still points at A.
        Assert.Equal(1, runtime.Entities.AliveCount);
    }

    [Fact]
    public void DestroyActor_unregisters_the_runtime_id_and_destroys_the_entity()
    {
        var runtime = new EntityRuntime();
        var id = runtime.CreateActor(1, 100, 0, 0, 0)!.Value;

        runtime.DestroyActor(100, id);

        Assert.False(runtime.Entities.IsAlive(id));
        Assert.False(runtime.RuntimeIds.TryResolve(100, out _));
    }
}

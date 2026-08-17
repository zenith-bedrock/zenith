# Zenith ECS — living architecture doc

**Status:** real, in production use since Phase XXI, expanded in Phase XXII. Authoritative for
Zombie, Minecart, Projectile, Cow, Skeleton, Spider. Not authoritative for anything else — see
[Scope](#scope) below.

This document describes what exists in `src/zenith/Ecs/` today. It is not a design proposal and
not a plugin API. Update it when the ECS surface changes; do not let it drift into aspiration.

Companion reading: [`docs/decisions.md` §106](decisions.md) records the Phase XXI acceptance
decision (why now, why this slice) without rewriting the earlier evidence-gated deferral (§99–102,
still historically accurate for the state of the project when they were written).
[`docs/phase-xxi-ecs-foundation-findings.md`](history/phases/phase-xxi-ecs-foundation-findings.md) and
[`docs/phase-xxii-ecs-roster-consolidation-findings.md`](history/phases/phase-xxii-ecs-roster-consolidation-findings.md)
have the benchmarks, DX comparisons and final-questions answers for each phase.
[`docs/entities.md`](entities.md) §11–§12 fold both phases into the long-running entity-catalog
narrative.

---

## Scope

**Migrated (ECS is the sole authoritative store):** Zombie, Minecart, Projectile (Phase XXI); Cow,
Skeleton, Spider (Phase XXII). Six species total.

**Not migrated (still the pre-existing OOP `IDamageableActor` + per-species `*Store` pattern):**
Creeper, Enderman, Bat, Villager, Golem, Fish — six species, still correctly served by
`GroundMobCombat`/`GroundMobMovement`/`DespawnLifecycle` and `IDamageableActor`. See
[Retirement gate](#retirement-gate-groundmobcombat-vs-damageableactorcombat) for why these six
specifically weren't pulled in this phase, and what would trigger migrating one.

**Never in scope:** Player, sessions, inventory, world storage, packets, transport. This is a
world-actor runtime, not a general application data model. See `ARCHITECTURE.md`'s "ECS decision
boundary" for the standing rule this phase operationalized for one concrete slice.

There is exactly one ECS-native combat helper, `DamageableActorCombat`
(`src/zenith/Gameplay/DamageableActorCombat.cs`), structurally parallel to the OOP `GroundMobCombat`
but reading components instead of an `IDamageableActor` object. Both exist simultaneously and will
continue to until the migrated slice covers enough of the roster that duplicating the pattern
outweighs maintaining two combat helpers — see [Retirement gate](#retirement-gate-groundmobcombat-vs-damageableactorcombat).

---

## Core primitives (`src/zenith/Ecs/`)

### `EntityId`

```csharp
readonly record struct EntityId(int Index, int Generation)
```

A slot index plus a generation counter. `Index` alone is not a safe handle — after an entity is
destroyed its `Index` is recycled by a later `Create()`. `Generation` bumps by one every time a
slot is destroyed, so a stale `EntityId` captured before the recycle compares unequal (via record
struct equality on both fields) to the new occupant's `EntityId`, and every store operation
independently re-validates liveness through `EntityWorld.IsAlive` rather than trusting the caller's
handle. `EntityId.Invalid = new(-1, 0)` is the explicit "no entity" value; `IsValid` checks
`Index >= 0`.

### `EntityWorld`

The index allocator. `Create()` reuses a freed index from an internal free-list (bumping that
slot's stored generation) before growing; `Destroy(id)` iterates every registered
`IComponentCleanup` store and calls `RemoveIfPresent(id)` *while the entity is still reported
alive* (cleanup needs `IsAlive` to still return true to do its own stale-handle checks correctly),
then marks the slot dead and bumps its generation. `IsAlive(id)` checks index bounds, the alive
flag, and generation match — all three, every call. `AliveCount`/`CreatedCount`/`DestroyedCount`
are debug/benchmark-only counters, not part of any hot path.

`EntityWorld` does not know about Bedrock runtime ids (that's `RuntimeIdIndex`) and does not know
about any specific component type (that's `ComponentStore<T>`, which registers itself with the
world's cleanup list on construction).

### `ComponentStore<T>` (sparse-set)

```csharp
sealed class ComponentStore<T> : IComponentCleanup where T : struct
```

Classic sparse-set: `int[] _sparse` maps `EntityId.Index → dense slot`, with parallel dense
`List<EntityId>` and `List<T>` arrays. `Set`, `TryGet`, `Has`, `GetRef`, `Remove` are all O(1).
`Remove` swap-removes the last dense element into the freed slot and updates that element's sparse
entry — the standard sparse-set trick, and the one place a stale-generation bug was found and
fixed during this phase (see [Errors and fixes](history/phases/phase-xxi-ecs-foundation-findings.md) in the
findings doc): every operation, including `Remove`, checks `_world.IsAlive(id)` first, so a stale
handle pointing at a slot since reused by a newer generation can never touch the new occupant's
component.

`Entities` (`IReadOnlyList<EntityId>`, internal) exposes the dense array directly for `Query` to
iterate — no copy, no snapshot.

Not a generic `Dictionary<EntityId, T>` wrapper: the dense array is what makes component
iteration (`Query`) a straight array walk instead of a dictionary enumeration.

### `Query` / `Query2<T1,T2>` / `Query3<T1,T2,T3>`

Allocation-free struct enumerators, hand-written per arity — no LINQ, no expression trees, no
runtime query compiler.

```csharp
foreach (var id in Query.With(smallerStore, biggerStore)) { ... }
```

**Driver semantics — explicit, not automatic:** the *first* type parameter's store is always the
one iterated; every other type is a pure `Has` filter. This was originally under-documented (the
field was even named `_shorterEntities`, implying an automatic "walk whichever is smaller"
optimization that the code never actually did) and was corrected during this phase — see the
findings doc's Addendum 1 fix. Put the smaller/rarer component first; the query still returns
correct results if you don't, just not the cheapest iteration, and that cost is visible at the
call site rather than hidden behind a runtime heuristic.

**Mutation rule:** do not add/remove a queried-type component on the entity currently being
visited mid-iteration — same rule as mutating a `List<T>` while foreach-ing it, because the dense
array a query walks *is* a `List<T>`. Mutating a different entity, or a non-queried component on
the current entity, is safe. There is no deferred command buffer in this first ECS.

Only 2- and 3-arity queries exist because no current system needs more.

### `ChunkSpatialIndex<T>` (`Gameplay/Entities/ChunkSpatialIndex.cs`, Phase XXVIII)

Not part of the ECS core — lives under `Gameplay/Entities/` since it's actor-gameplay acceleration,
not component storage — but built to sit directly on top of `Query`'s output. A small chunk-bucketed
(16×16 block, `ChunkMath.BlockToChunk`) candidate index: `Insert(item, x, z)`, `Clear()`,
`EnumerateNearby(x, z, radius)` (allocation-free struct enumerator, same idiom as `Query2`/`Query3`
above). It answers "which candidates occupy these nearby chunks?" only — every exact
distance/height/state check stays with the caller, and it never decides gameplay. It is a **derived**
structure, rebuilt from whichever store owns position truth (never incremental — see the findings
doc for why), not authoritative storage in its own right.

Two instances exist today: an `EntityId`-keyed one private to `ProjectileSystem` (ECS damageable
actors, rebuilt once per `Tick` from `Query.With(Health, Positions)`), and a `Player.Player`-keyed
`PlayerSpatialIndex` shared across systems, rebuilt by `PlayerSpatialIndexSystem` right after
`MovementSystem`. Full audit, ownership rationale, and before/after benchmarks:
[`phase-xxviii-spatial-query-scaling-findings.md`](history/phases/phase-xxviii-spatial-query-scaling-findings.md).

### `RuntimeIdIndex`

```csharp
sealed class RuntimeIdIndex
{
    public bool Register(ulong actorRuntimeId, EntityId id);  // false = duplicate, no overwrite
    public bool TryResolve(ulong actorRuntimeId, out EntityId id);
    public void Unregister(ulong actorRuntimeId);              // safe no-op if unmapped
}
```

Bedrock-runtime-id → `EntityId` lookup — the hot path for incoming packets that reference an actor
by its wire id (melee/projectile hit resolution, interaction). Deliberately keyed on `ulong`
matching `ActorIdentity.ActorRuntimeId`'s real type, *not* `long` — an earlier version of
`EntityRuntime.CreateActor` registered the wrong id (`ActorUniqueId` instead of
`ActorRuntimeId`), a bug masked entirely by every actor allocating both ids from the same numeric
source. Making the key type match the real field made passing the wrong id a compile error rather
than a coincidence. See the findings doc, Addendum 2 item 1.

### `EntityRuntime` (formerly `EntityStores`)

The one owning, cohesive access-boundary object — not a service locator, not global state:

```csharp
sealed class EntityRuntime
{
    public EntityWorld Entities { get; }
    public RuntimeIdIndex RuntimeIds { get; }
    public ComponentStore<Position> Positions { get; }
    public ComponentStore<Velocity> Velocities { get; }
    public ComponentStore<HealthComponent> Health { get; }
    public ComponentStore<ActorIdentity> Identities { get; }
    public ComponentStore<DespawnTracking> Despawn { get; }

    public EntityId? CreateActor(long actorUniqueId, ulong actorRuntimeId, float x, float y, float z, float yaw = 0f);
    public void DestroyActor(ulong actorRuntimeId, EntityId id);
    internal string Describe(EntityId id); // debug-only
}
```

Bundles the world, the runtime-id index, and the 5 components every migrated actor category
shares. `CreateActor` is transactional: it allocates the entity, attaches `Position` and
`ActorIdentity`, then attempts `RuntimeIds.Register`; on failure (duplicate runtime id — should
never happen in practice since ids come from `PlayerManager.AllocateRuntimeId`, but the contract
must hold regardless) it rolls back via `Entities.Destroy(id)` and returns `null`, leaving no
partially-created entity behind. Callers do `_stores.CreateActor(...) ?? throw new
InvalidOperationException(...)`.

Renamed from `EntityStores` during this phase — the old name undersold that it *also* owns
`CreateActor`/`DestroyActor`/`Describe`, not just storage. `World` was considered and rejected
(collides with the existing gameplay `Zenith.World.World`); `EntityRuntime` was picked as the
least overloaded option.

---

## Shared components (`src/zenith/Ecs/Components.cs`)

All `struct`, default (internal) accessibility.

| Component | Fields | Shared by |
|---|---|---|
| `Position` | `X, Y, Z, Yaw` | Zombie, Minecart, Projectile |
| `Velocity` | `X, Y, Z` | Zombie (knockback), Minecart (rolling+steering), Projectile (ballistic), and — since Phase XXIX — every ECS ground mob's `.Y` reused as gravity fall-speed (Zombie, Cow, Skeleton, Spider) — shared *data*, per-system behavior |
| `HealthComponent` | `HealthState State` | Zombie, Minecart (wraps the existing `HealthState` class — one authoritative instance) |
| `ActorIdentity` | `ActorUniqueId (long), ActorRuntimeId (ulong)` | Zombie, Minecart, Projectile — Bedrock wire identity, deliberately separate from `EntityId` |
| `DespawnTracking` | `LastSeenNearPlayerTick (ulong)` | Zombie, Minecart only — **not** Projectile, which uses pure-age lifetime (`ProjectileState.AgeTicks`), a deliberate non-universal-lifecycle choice consistent with `docs/entities.md`'s "no fake universal components" rule |

`Position` is deliberately not called `Transform` — `Yaw` is its only rotation field, since only
Zombie/Minecart ever read it. Projectile has no meaningful yaw concept at 20 TPS ballistic update
and doesn't carry one.

## Feature-specific components

Each owned by exactly one system, not shared — sharing was rejected wherever the underlying data
only *looked* similar but the behavior was owned by one feature:

- `ZombieState { TargetPlayerRuntimeId (long?), NextAttackTick (ulong) }`
- `VehicleOccupancy { OccupantPlayerRuntimeId (long?) }`
- `ProjectileState { OwnerRuntimeId (long), AgeTicks (ulong) }`
- `CowState { WanderDirectionX/Z (float), WanderChangeAtTick (ulong), BreedCooldownUntilTick (ulong) }` (Phase XXII)
- `SkeletonState { NextShotTick (ulong) }` (Phase XXII)
- `SpiderState { TargetPlayerRuntimeId (long?), NextAttackTick (ulong) }` (Phase XXII — same shape as `ZombieState` by construction; kept separate since nothing queries "any entity with a retained target" across species)

Cow/Skeleton/Spider originally carried no `Velocity` component — none of the three read or wrote
`X`/`Z` (no knockback, no rolling motion, no ballistic integration). Phase XXIX attached it to all
three anyway, reusing only `.Y` as gravity fall-speed: every ECS ground mob needed the same per-tick
downward-speed state, `Velocity` already existed as exactly that shape of shared data, and adding a
second component for one float would have been the "fake universal component" this rule warns
against, not avoiding it. See `docs/history/phases/phase-xxix-ground-actor-physics-findings.md`.

### Ground locomotion (`GroundMobMovement`, Phase XXIX)

Ground mob movement is resolved through `Gameplay/Entities/GroundMobMovement.cs`, a stateless
resolver over primitives (world + position + desired delta), not an ECS system — both the ECS roster
above and the legacy roster (`Villager`/`Golem`/`Creeper`, plain classes with a `VerticalFallSpeed`
field standing in for `Velocity.Y`) call the same two static methods, `TryMoveHorizontal` (occupancy +
one-block step-up, no support requirement) and `ResolveVertical` (gravity/falling/landing). Real
physical support/occupancy/fluid classification lives on `Blocks` (`IsAir`/`IsFluid`/`CanOccupy`/
`CanSupportGroundActor`/`BlocksMovement`), replacing the old "any non-air block is solid ground"
assumption. Minecart's rolling fallback and Enderman's teleport-landing check use the narrower
`IsSupportedGroundCell` instead — neither walks continuously or needs a vertical model. See the phase
findings doc for the full research and design rationale.

---

## Systems

`ZombieSystem`, `MinecartSystem`, `ProjectileSystem`, `CowSystem`, `SkeletonSystem`, `SpiderSystem`
remain one cohesive system per species/category — not fragmented into per-behavior micro-systems
(movement-system, combat-system, despawn-system per mob). Each owns its own feature
`ComponentStore<T>` (e.g. `ZombieSystem` owns
`ComponentStore<ZombieState>`) built against the shared `EntityRuntime` passed into its
constructor, and exposes:

- `internal IReadOnlyList<EntityId> Zombies` (etc.) — the live set, for tests/diagnostics/other
  systems.
- `internal bool Owns(EntityId id)` — presence check via its feature store, used for cross-category
  dispatch (see below).
- `internal EntityRuntime Stores` + a feature-component accessor (e.g. `ZombieStates`) — for
  test/diagnostic reads only, not meant as a general public surface.
- A narrow composition helper (`SpawnZombie(x, y, z)`, `SpawnMinecart(x, y, z)`) — the intended
  common workflow is `var id = system.SpawnZombie(...);`, not a fluent `EntityBuilder` DSL.

### Per-tick allocation

Each system reuses a `private readonly List<EntityId> _tickScratch = [];` field
(`.Clear()` + `.AddRange(store.Entities)` per tick) instead of allocating a fresh `EntityId[]`
snapshot every tick — `List<T>.Clear()` retains its backing array, so steady-state ticking is
allocation-free from entity enumeration itself. This replaced an earlier version that allocated a
fresh array every tick, flagged as a 20-TPS-times-many-systems-times-many-actors GC risk before it
shipped.

### Cross-category dispatch: `DamageDispatch` (Phase XXII)

`ProjectileSystem.FindHitDamageableActor` queries `Query.With(_stores.Health, _stores.Positions)`
(Health drives — smaller set, since Projectiles never carry `HealthComponent`) — this finds
*candidate* targets purely by component presence, unchanged since Phase XXI. Routing a hit to the
right system's `TryApplyDamage` used to be a hand-written `if (_zombies.Owns(id)) ... else if
(_minecarts.Owns(id)) ...` chain, explicitly documented as acceptable only until a third
ECS-damageable category arrived. Cow/Skeleton/Spider's migration this phase crossed that bar (five
ECS-damageable species besides Projectile itself), so the chain was replaced with
`src/zenith/Gameplay/DamageDispatch.cs`:

```csharp
sealed class DamageDispatch
{
    public void Register(Func<EntityId, bool> owns, Func<EntityId, DamageSource, float, IReadOnlyList<Player.Player>, bool> tryApplyDamage);
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online);
}
```

Each ECS-damageable system registers its own `(Owns, TryApplyDamage)` pair once, at composition-root
time (`ZenithServer.cs`) — one line per species, visible at startup. `ProjectileSystem` now takes a
`DamageDispatch` instead of concrete `ZombieSystem`/`MinecartSystem` references, so it no longer
knows about any specific species at all — a genuine decoupling, not just a bigger `if` chain.

**This is deliberately not a generic event bus.** It is a fixed list built once and never mutated
after startup — no runtime registration, no reflection, no dynamic discovery. What it eliminates is
species-name growth in `ProjectileSystem` itself; it does not become a general "any system can
subscribe to any event" framework, and nothing else in the codebase should reach for it as one.
Death consequences (loot item, dismount, knockback, ...) stay entirely feature-owned inside each
system's own `TryApplyDamage` — `DamageDispatch` only answers "which system's `TryApplyDamage` do I
call for this entity," never "what happens when I call it."

---

## Structural mutation rules

- **Creation/destruction are entity-centric commands**, not component-store calls scattered across
  callers: `EntityRuntime.CreateActor`/`DestroyActor`, `EntityWorld.Create`/`Destroy`.
- **Structural changes apply immediately.** There is no deferred command buffer in this ECS —
  `Destroy` runs its cleanup synchronously, in the calling context (the tick). This is safe today
  because nothing destroys an entity from inside another entity's component iteration in a way
  that would corrupt that iteration (see the Query mutation rule above) — if that ever becomes
  necessary, a command buffer is the fix, not a workaround at each call site.
- **One authoritative destruction path per feature.** Feature consequences — loot drop, XP award,
  rider dismount, replicated-removal packet fan-out — happen *before* `DestroyActor` is called;
  `EntityRuntime`/`EntityWorld` then own pure structural teardown (component removal, slot
  freeing, generation bump). See `MinecartSystem.TryApplyDamage`'s doc comment for the concrete
  pattern this produced: capture any data you need *before* the call that might destroy the
  entity (e.g. the rider's `Player` reference), because component reads after `Destroy` fail —
  the component is gone, not stale.
- **Query iteration invalidation**: see the `Query` mutation rule above — do not add/remove a
  queried component on the entity currently being visited.

---

## Debuggability

`EntityRuntime.Describe(EntityId id)` — a debug-only, non-hot-path method that reports whether an
id is alive and which of the 5 shared stores contain it, without reflection. Intended for use from
tests or ad-hoc diagnostics, not from any per-tick code path. There is no generic "enumerate every
component on every entity" reflection facility, deliberately — that would be exactly the kind of
plugin-API-shaped machinery this phase explicitly deferred.

---

## What this ECS deliberately does not have

Carried over unchanged from the Phase XXI brief — not because these are hard, but because nothing
has needed them yet:

- **Archetypes.** All migrated components are looked up per-store; there is no contiguous
  same-shape-entity storage. See the findings doc's answer to "are archetypes now justified?" —
  no.
- **A job scheduler / parallel execution.** Every system still runs single-threaded, in
  `GameLoop`'s deterministic registration order, same as every other `IGameSystem`.
- **Source generation or reflection-driven components.** Every `ComponentStore<T>` is a plain
  generic instantiation wired by hand in `EntityRuntime`'s constructor.
- **A public plugin API.** `EntityId`, `ComponentStore<T>`, `Query` etc. are all default
  (internal) accessibility. Nothing here is meant to be exposed to external code, now or as a
  stated near-term goal.
- **A generic damage/event framework.** See [Cross-category dispatch](#cross-category-dispatch-damagedispatch-phase-xxii) above.

---

## Retirement gate: `GroundMobCombat` vs. `DamageableActorCombat`

Both combat helpers are real and both stay. `GroundMobCombat<TMob>` (generic over
`IDamageableActor`) now serves the 6 unmigrated species: Creeper, Enderman, Bat, Villager, Golem,
Fish. `DamageableActorCombat` (entity-id + `EntityRuntime`) serves the 6 ECS-authoritative species:
Zombie, Minecart, Cow, Skeleton, Spider, Projectile (Projectile deals damage, never receives it, so
it never calls either helper as a target — it calls `DamageableActorCombat.TryApplyDamage` on
whatever it hits, via `DamageDispatch`). Roster parity (6/6) was reached this phase.

**Phase XXII revisited this gate and chose Outcome A: keep both helpers.** The trigger stated in
Phase XXI — "the migrated roster covers enough of the remaining species that maintaining two
helpers costs more than migrating the stragglers" — has *not* fired, evaluated honestly against the
6 remaining species' real behavior, not just their count:

| Species | What makes it non-mechanical to migrate |
|---|---|
| Creeper | Fuse timer + area explosion — a lifecycle/damage shape neither `GroundMobCombat` nor `DamageableActorCombat` currently models (an actor that damages an *area* on its own death, not on request) |
| Enderman | Damage-triggered aggro (not proximity-scan like Zombie/Spider) + teleport — a fourth distinct targeting shape already noted in `entities.md` |
| Bat | 3D wander (the only flight movement model — `Position.Yaw` alone doesn't carry pitch) |
| Villager | Real trade interaction (`Wares`, a `List<StackId>` with no ECS analog yet — the first and only non-player inventory-shaped data) |
| Golem | Boss phases (`IsEnraged`, a second ability) — the one species with per-tick behavior branching beyond attack/chase |
| Fish | Swim movement validity (`TrySwim`, a third distinct movement-validity predicate) |

None of these are "just port the same shape" work — each still has genuinely distinct data or
behavior the way Phase XIV–XX's evidence trail already established. Forcing them through this
phase's window to hit a roster-parity number would have been "migrate to fill a table," explicitly
rejected as a goal by this phase's own brief.

### Per-species re-evaluation tracker

The "migrate when gameplay work already needs to touch it" trigger above requires knowing, per
species, what kind of touch counts. Phase XXIX's ground-actor gravity/support rewrite already
touched 5 of these 6 files (Creeper, Enderman — teleport-landing check only, Fish, Golem,
Villager; Bat was deliberately excluded, it never had a vertical model). That was shared physics
infrastructure, not species-specific pressure — no bug was fixed in one species and missed in
another, no feature needed identical behavior across species and got written twice — so the gate
did **not** fire for any of them from that alone. This table exists so the next PR that touches one
of these species doesn't have to re-derive that judgment from scratch:

| Species | Touched in | Re-evaluate when |
|---|---|---|
| Creeper | Phase XXIX (gravity/ground-support rewrite; shared infra, not species-specific) | another entity needs area-damage-on-death |
| Enderman | Phase XXIX (partial — teleport-landing check only, not the full gravity resolver) | another entity needs damage-triggered aggro or teleport |
| Bat | Aug 2026 domain reorg only; excluded from Phase XXIX by design | any species needs a flight/3D-wander movement model |
| Villager | Phase XXIX (gravity/ground-support rewrite; shared infra, not species-specific) | a trade UI / NPC shop needs non-player inventory-shaped data |
| Golem | Phase XXIX (gravity/ground-support rewrite; shared infra, not species-specific) | another entity needs per-tick phase/state branching |
| Fish | Phase XXIX (partial — swim-validity path is distinct from the gravity resolver) | another aquatic entity (e.g. squid) needs swim physics |

No migration is proposed or scheduled by this table — it is evidence bookkeeping only, so the
retirement trigger stays evaluable without re-reading git history each time.

**No maintenance-cost signal has appeared either**: no bug has been fixed in one combat helper and
missed in the other; no feature has needed to work identically across both and been written twice.
`DamageDispatch` (below) absorbed the one real cross-cutting pressure point (Projectile's target
dispatch) without touching either combat helper.

**Updated retirement trigger, going forward:** migrate a remaining species *when gameplay work
already needs to touch it* (a bug fix, a feature request, a new pressure test) — not on a schedule,
and not to close this gate for its own sake. Re-evaluate this gate the next time either combat
helper changes for a reason that would have applied to both, or when the legacy roster drops to 3
or fewer (parity-minus-one, the point where maintaining a second helper for a shrinking minority
starts to look wasteful on its own). Until then, treat the dual-helper period as a stable, expected
state — not a to-do.

---

## Adding a new ECS actor (worked template)

The intended common-case workflow, demonstrated concretely rather than described abstractly:

```csharp
// 1. Composition helper on the owning system:
internal EntityId SpawnWidget(float x, float y, float z)
{
    var id = _stores.CreateActor(_players.AllocateRuntimeId(), (ulong)nextUniqueId, x, y, z)
        ?? throw new InvalidOperationException("Widget spawn failed.");
    _stores.Health.Set(id, new HealthComponent { State = new HealthState(maximum: 10f) });
    _stores.Velocities.Set(id, new Velocity());
    _stores.Despawn.Set(id, new DespawnTracking());
    _widgets.Set(id, new WidgetState());
    return id;
}

// 2. Tick, using the reused scratch list:
public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
{
    _tickScratch.Clear();
    _tickScratch.AddRange(_widgets.Entities);
    foreach (var id in _tickScratch)
    {
        // despawn / behavior / combat as needed, reading via _stores.Positions.GetRef(id) etc.
    }
}
```

No custom `Store` class, no manual per-component cleanup loop (the 5 shared stores are already
registered with `EntityWorld`'s cleanup list; a feature store like `_widgets` self-registers on
construction the same way), no runtime-id plumbing beyond the one `CreateActor` call, no
viewer-membership boilerplate beyond calling the existing `ViewerReconciliation.Sync` helper (unchanged
by this phase — still the replication seam for every category, ECS or not). See the findings doc's
answer to the Phase XXI Addendum 2 "closing DX test" for the full comparison against what this
would have required under the pre-ECS `*Store` pattern.

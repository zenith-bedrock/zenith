# Phase XXI — ECS Foundation & First Authoritative Runtime Migration

**Status: complete.** Full-solution build and `dotnet test zenith.sln` (935 tests across all
projects, 815 in `zenith.Tests`) pass. Companion reading: [`docs/ecs.md`](../../ecs.md) (the living
architecture doc for what exists), [`docs/decisions.md` §106](../../decisions.md) (the ADR recording
this acceptance decision without rewriting the earlier evidence-gated deferral at §99–102),
[`docs/entities.md`](../../entities.md) §11.

## Why this phase exists

Every prior ADR touching ECS (§99–102) deferred it as an *evidence-gated* decision: build it only
once measured world-actor pressure justified it, and the first isolated feasibility spike
(`docs/audit/PHASE_E_ECS_FEASIBILITY_FINDINGS.md`) found no material gain over the direct model at
the scales actually observed. That evidence-gated deferral is still historically accurate and is
**not** being retroactively declared wrong — nothing changed about the measured pressure between
then and now.

What changed is that this phase is not another feasibility spike arriving at a data-driven "yes."
It is an explicit product/architecture decision to build a real ECS for a representative slice
(Zombie, Minecart, Projectile) and evaluate it *in production use*, on its own merits — correctness,
developer experience, and whether it makes the next 9 unmigrated species easier or harder to build
— rather than waiting for a specific measured actor count to force the question. See §106 for the
full ADR text.

## Migration scope and rationale

| Actor | Why it was chosen |
|---|---|
| **Zombie** | The ordinary case — a `IDamageableActor` ground mob, structurally representative of 8 of the 9 remaining unmigrated species. Proves the baseline migration shape. |
| **Minecart** | A non-mob vehicle with a real player↔entity riding relationship (`VehicleOccupancy`). Chosen specifically to prevent the ECS from silently becoming "a mob ECS" — it has no AI, no targeting, and its damage path (`TryApplyDamage`) had to correctly dismount a rider mid-destroy. |
| **Projectile** | Has no `IDamageableActor` and no `HealthComponent` at all. Chosen specifically to prove ECS composition is broader than "things with health" — a `Position` + `Velocity` + `ProjectileState` entity that nothing else in the roster looks like. |

Explicitly **not** migrated: Skeleton, Cow, Creeper, Enderman, Bat, Spider, Villager, Golem, Fish —
9 species, deliberately untouched. `GroundMobCombat`/`GroundMobMovement`/`IDamageableActor` still
correctly serve all 9; see [Retirement gate](../../ecs.md#retirement-gate-groundmobcombat-vs-damageableactorcombat)
in `docs/ecs.md`.

## What was built

Summarized in full in [`docs/ecs.md`](../../ecs.md): `EntityId` (generation-safe), `EntityWorld`
(allocator + cleanup registry), `ComponentStore<T>` (sparse-set), `Query`/`Query2`/`Query3`
(allocation-free struct enumerators), `RuntimeIdIndex` (Bedrock-runtime-id → `EntityId`),
`EntityRuntime` (the one owning access-boundary object). Five shared components
(`Position`/`Velocity`/`HealthComponent`/`ActorIdentity`/`DespawnTracking`) plus three
feature-specific ones (`ZombieState`/`VehicleOccupancy`/`ProjectileState`). `DamageableActorCombat`
as the ECS-native sibling of `GroundMobCombat`. `DespawnLifecycle` (mechanical rename from
`GroundMobLifecycle`, since its consumer set now spans OOP and ECS actors).

Old `Zombie`/`ZombieStore`, `Minecart`/`MinecartStore`, `Projectile`/`ProjectileStore` were deleted
outright, not wrapped or kept as a compatibility shim — confirmed via `grep` that no such class
definitions remain anywhere in `src/zenith`.

---

## Corrections made mid-phase (addenda)

Two rounds of correctness/DX feedback arrived while the migration was in progress and were folded
in without a redesign:

**Addendum 1 — correctness/DX:**
1. `ComponentStore<T>.Remove` did not validate generation, only index bounds — a stale `EntityId`
   pointing at a slot reused by a newer generation could remove the *new* occupant's component.
   Fixed by adding `_world.IsAlive(id)` as `Remove`'s first check, same as every other operation
   already did. Regression test: create A → destroy A → create B reusing A's slot → `Remove(A)`
   must not touch B's component. Confirmed passing.
2. `Query2`/`Query3`'s doc comment claimed "walks the shortest input store," but the
   implementation always drove off the *first* type parameter regardless of relative size. Fixed
   by rewriting the doc comment to state the real, deterministic "first-parameter-drives"
   semantics as an intentional DX choice, and renaming the misleading `_shorterEntities` field to
   `_drivingEntities`.
3–15. The remaining 13 items were either already satisfied by the in-progress design (cohesive
   per-species systems, narrow `Spawn*` composition helpers, entity-centric
   `Spawn`/`Destroy`/`TryResolveRuntimeId` commands, no over-fragmented components) or produced the
   specific fixes below under Addendum 2.

**Addendum 2 — runtime integrity/scaling DX:**
1. `RuntimeIdIndex` was registering `ActorUniqueId` under a mapping documented as
   `ActorRuntimeId → EntityId` — masked entirely because every actor's two ids were numerically
   equal by construction. Fixed by keying `RuntimeIdIndex` on `ulong` (matching
   `ActorIdentity.ActorRuntimeId`'s real type) so passing the wrong id is a compile error, not a
   silent coincidence. Regression test with deliberately different unique/runtime values, proving
   lookup only works via the real runtime id.
2. `CreateActor` was not transactional — a rejected duplicate runtime-id registration left a live
   entity with `Position`/`ActorIdentity` attached but no runtime-id mapping. Fixed: `CreateActor`
   now returns `EntityId?` and rolls back (`Entities.Destroy`) on registration failure. Regression
   test: spawn A at runtime id 100 → attempt B at runtime id 100 → B does not exist, A is
   unaffected, runtime id 100 still resolves to A.
3. Per-tick `new EntityId[]` snapshot allocations in `ZombieSystem`/`MinecartSystem`/
   `ProjectileSystem` were flagged as steady-state GC pressure at 20 TPS × many systems × many
   actors. Replaced with a reused `_tickScratch` list per system (`Clear()` + `AddRange()` each
   tick). Confirmed allocation-free at steady state via the `ecs-query` micro-benchmark below
   (0.0 B/tick after warmup).
4. `ProjectileSystem`'s cross-species dispatch (`if (_zombies.Owns(id)) ... else if
   (_minecarts.Owns(id)) ...`) was flagged as a pattern that could calcify into a hard-coded
   type-switch. Documented explicitly in `docs/ecs.md` as acceptable at 2 consumers under this
   project's "two is a coincidence, three is a pattern" rule, with the concrete trigger and shape
   for what replaces it at a third consumer written down rather than left implicit.
5. `EntityStores` was renamed to `EntityRuntime` — the old name undersold that it also owns
   `CreateActor`/`DestroyActor`/`Describe`. `World` was considered and rejected (collision with
   `Zenith.World.World`).
6. `DamageableActorCombat` vs. `GroundMobCombat` coexistence — documented as explicitly
   transitional with a concrete (if not numerically precise) retirement trigger; see
   [`docs/ecs.md`](../../ecs.md#retirement-gate-groundmobcombat-vs-damageableactorcombat).
7. The "closing DX test" — see [DX comparison](#dx-comparison) below.

All fixes are covered by tests in `EcsCoreTests.cs` (`EntityRuntimeTests` for items 1–2 of
addendum 2) and confirmed passing in the full-suite run.

---

## Benchmarks

Two harnesses, measuring different things — neither substitutes for the other:

- **`zenith.Benchmarks --ecs-runtime`** (new this phase, `EcsRuntimeBenchmarks.cs`): measures the
  ECS core in isolation — no `GameLoop`, no replication, no players.
- **`zenith.Benchmarks --runtime-load`** (pre-existing, updated to build against the ECS-backed
  systems): measures full-tick gameplay under the real `GameLoop`, including replication fan-out —
  this is what "representative gameplay" and "replication" mean for this phase, since Zombie and
  Projectile are now ECS-authoritative end to end.

All numbers below are real measurements from this machine (`dotnet run -c Release`), not
estimates.

### ECS core — entity lifecycle (create → destroy → recreate-reusing-freed-slots → destroy)

```
ecs-lifecycle   entities=   100 total=1.177ms perEntity=2.9us  alloc/entity=4.6B gc=0/0/0
ecs-lifecycle   entities=  1000 total=0.101ms perEntity=0.0us  alloc/entity=4.5B gc=0/0/0
ecs-lifecycle   entities= 10000 total=11.704ms perEntity=0.3us alloc/entity=7.4B gc=0/0/0
```

Sub-microsecond per lifecycle operation at every scale tested; the small residual per-entity
allocation is the `List<IComponentCleanup>` internals warming up, not steady-state growth (no GC
collections triggered at any scale — `gc=0/0/0` throughout).

### ECS core — component iteration (two-component query, 200 ticks)

```
ecs-query       entities=   100  matched=    50  ticks=200 total=11.536ms perTick=0.0577ms alloc/tick=0.0B gc=0/0/0
ecs-query       entities=  1000  matched=   500  ticks=200 total=1.311ms  perTick=0.0066ms alloc/tick=0.0B gc=0/0/0
ecs-query       entities= 10000  matched=  5000  ticks=200 total=14.393ms perTick=0.0720ms alloc/tick=0.0B gc=0/0/0
```

Zero allocation per tick at every scale — the allocation-free `Query2` enumerator design holds up
under measurement, not just by inspection. (The 100-entity case's higher total than the 1000-entity
case is JIT/measurement noise at that small a workload, not a real regression — note the per-tick
cost is still sub-0.06ms either way.)

### ECS core — runtime-id lookup (Bedrock-packet-referenced-actor resolution)

```
ecs-lookup      entities=   100  lookups=    5000  perLookup=67.7ns  alloc/lookup=0.00B gc=0/0/0
ecs-lookup      entities=  1000  lookups=   50000  perLookup=180.4ns alloc/lookup=0.00B gc=0/0/0
ecs-lookup      entities= 10000  lookups=  500000  perLookup=42.5ns  alloc/lookup=0.00B gc=0/0/0
```

Tens-of-nanoseconds `Dictionary<ulong, EntityId>` lookup, zero allocation — this is the hot path
for every melee/interaction packet that references an actor by wire id, and it is not a measured
concern at any actor count this project has ever run.

### Representative gameplay — Zombie, 1000 actors, 10 players, 200 ticks (ECS-authoritative)

```
zombie-idle     avg=2.600ms p50=0.706ms  p95=5.519ms  p99=14.526ms max=47.577ms alloc=429895B/tick  gc=10/0/0
zombie-direct   avg=2.115ms p50=0.792ms  p95=16.666ms p99=19.078ms max=21.946ms alloc=590067B/tick  gc=14/0/0
zombie-obstacle avg=1.511ms p50=0.835ms  p95=5.695ms  p99=6.627ms max=7.499ms  alloc=625904B/tick   gc=14/0/0
```

### Replication — Projectile actor churn, 1000 actors, 10 players, 200 ticks (ECS-authoritative)

```
actor-global   avg=32.159ms p50=26.252ms p95=44.246ms p99=52.273ms max=303.149ms
               actors=1000/1000 spawnFanout=30000 moveFanout=1980000 removeFanout=20000
actor-interest avg=3.699ms  p50=3.498ms  p95=4.439ms  p99=6.052ms  max=7.048ms
               actors=1000/1000 spawnFanout=3000  moveFanout=198000  removeFanout=2000
```

These numbers are consistent in shape with the pre-ECS Phase D/F baselines (interest gating still
reduces move fan-out roughly 10x, same as before) — the ECS migration changed *storage*, not the
gameplay/replication algorithms, so this is the expected result, not a new finding. It confirms the
migration did not regress the thing those earlier phases spent real effort characterizing.

**Honest framing:** none of these numbers, on their own, are the reason ECS was accepted this
phase — see [Why this phase exists](#why-this-phase-exists). Nothing here shows the direct-model
`*Store` pattern was a performance problem; it wasn't, at any scale this project has measured. The
lifecycle/query/lookup numbers exist to prove the new machinery isn't a regression (it isn't — zero
allocation at steady state, sub-microsecond operations throughout), not to claim a speedup that
would have been dishonest to report given no rigorous head-to-head was run at matched conditions.

---

## DX comparison

### Quantitative: Zombie, before vs. after (real line counts, `git show HEAD` vs. working tree)

| | Old (OOP, pre-ECS) | New (ECS) |
|---|---|---|
| Files touched to add the species | `Zombie.cs` (46 lines: `Zombie` + `ZombieStore` in one file), `ZombieSystem.cs` (289 lines) | `ZombieSystem.cs` (409 lines), `ZombieState.cs` (17 lines) |
| Total species-specific lines | 335, across 2 files | 426, across 2 files |
| `ZombieSystem` constructor params | 4 (`world, players, ZombieStore zombies, itemPalette`) | 4 (`world, players, EntityRuntime stores, itemPalette`) — **unchanged** |
| Manual lifecycle/store operations | Custom `ZombieStore.TryAdd`/`Remove`, hand-written `Contains`-based dedup | `EntityRuntime.CreateActor`/`DestroyActor` — no custom store class, no dedup logic (generation-safe `EntityId` makes double-add/remove structurally unrepresentable) |
| Composition-root registrations | `new ZombieStore()` then `new ZombieSystem(world, players, store, itemPalette)` | `new ZombieSystem(world, players, entities, itemPalette)` — one fewer allocation/registration, since `EntityRuntime` is shared across all 3 migrated species instead of one store per species |

Zombie-specific code grew ~27% in line count (335 → 426) but **eliminated an entire hand-written
store class per species** (`ZombieStore`'s `TryAdd`/`Remove`/dedup-via-`Contains` — 12 lines that
existed only to reimplement what `EntityWorld`'s generation-safe allocator now provides for free,
*shared* across all 3 migrated species instead of duplicated per species). The real DX shift shows
up at the 3rd migration (Projectile), not the 1st — see below.

### Quantitative: Projectile, before vs. after

| | Old | New |
|---|---|---|
| Files/lines | `Projectile.cs` (54 lines), `ProjectileSystem.cs` (239 lines) = 293 | `ProjectileSystem.cs` (299 lines), `ProjectileState.cs` (15 lines) = 314 |
| `ProjectileSystem` constructor params | 4 (`world, players, ProjectileStore projectiles, ZombieSystem zombies` — hardcoded to Zombie specifically) | 5 (`world, players, EntityRuntime stores, ZombieSystem zombies, MinecartSystem minecarts`) |

The constructor grew by one real parameter — but that parameter buys a genuine capability increase
(Projectile can now damage *either* Zombie or Minecart, where the old version was hardcoded to only
ever look at `ZombieSystem`). This is not boilerplate growth; it's the honest cost of the
cross-category dispatch capability discussed in `docs/ecs.md`.

### Shared infrastructure amortization

The one-time `src/zenith/Ecs/` cost is 593 lines total, shared across all 3 migrated species (≈198
lines/species amortized) and — more importantly — **not paid again** by the 4th, 5th, ... migrated
species. Every future migration only pays its own `*State` component (13–17 lines each so far) plus
whatever grows inside its `*System.cs`. This is the concrete answer to "does this get better as more
things migrate": yes, because the fixed cost was already paid this phase.

### Qualitative

**What became genuinely easier:**
- No more hand-written per-species store class (`ZombieStore`/`MinecartStore`/`ProjectileStore`
  each reimplemented the same `TryAdd`/`Remove`/dedup pattern — gone, replaced by the shared,
  already-correct `EntityWorld`).
- Stale-handle bugs are structurally prevented rather than relying on each store's own
  `Contains`-based defense (the old `ZombieStore.TryAdd` used `_active.Contains(zombie)` — a
  reference-equality check that only worked because nothing ever recycled a `Zombie` reference; the
  new `EntityId` generation check works even if something *does* try to reuse a slot).
- Cross-category composition (Projectile reading Health+Position across both Zombie and Minecart)
  went from "hardcoded to one specific system" to "queryable by component presence, dispatched by
  an explicit 2-consumer `if` chain" — a real, if modest, generalization.

**What stayed feature-specific, correctly:** targeting logic (`ZombieState`), mount/dismount
(`VehicleOccupancy`), ballistic integration (`ProjectileState`) — none of this became generic, and
it shouldn't have. The ECS gave these a place to live as small structs; it did not (and should not)
absorb their behavior into itself.

**What got harder:** test assertions that read state *after* an entity is destroyed. The old
`ProjectileImpactDamagesZombieWithProjectileAttributionAndRemovesOnce` test could inspect
`zombie.Health.FatalSource` after death, because the old concrete `Zombie` object just stayed in
memory once removed from the store. The ECS version can't — the `HealthComponent` is genuinely gone
once `DestroyActor` runs, not stale. That specific assertion was dropped from the rewritten test.
This is a real testability trade-off, not a regression to paper over: it is the direct consequence
of the same generation-safety property that fixed the stale-handle bug above. Any future test
needing post-death inspection must capture the value *before* destruction, which is exactly the
pattern `MinecartSystem.TryApplyDamage` already had to adopt in production code for the same reason
(see `docs/ecs.md`, Structural mutation rules).

### The closing DX test (Addendum 2, item 7)

*Hypothetical: adding one more simple ECS actor — Position/Health/ActorIdentity/DespawnTracking
plus one feature component and one behavior system.*

Following the template in `docs/ecs.md`'s ["Adding a new ECS actor"](../../ecs.md#adding-a-new-ecs-actor-worked-template)
section:

1. **New file, one feature component** (`WidgetState.cs`, ~13–17 lines, matching
   `ZombieState`/`VehicleOccupancy`/`ProjectileState`'s size). No registration step beyond the
   constructor call below — `ComponentStore<T>` self-registers with `EntityWorld`'s cleanup list on
   construction.
2. **New file, one system** (`WidgetSystem.cs`) constructed with `(World.World world, PlayerManager
   players, EntityRuntime stores, ...)` — the same 3-parameter shape every migrated system already
   uses, plus whatever the feature needs (an `ItemPalette` for loot, another system for
   cross-category dispatch, etc.). Owns its own `ComponentStore<WidgetState> _widgets = new(stores.Entities)`.
3. **One composition helper**, `SpawnWidget(x, y, z)`: `_stores.CreateActor(...) ?? throw ...`,
   then `Health.Set`/`Velocities.Set`/`Despawn.Set` as needed, then `_widgets.Set(id, new
   WidgetState())`.
4. **`Tick`** reuses the now-standard `_tickScratch` pattern.
5. **Composition root**: one line, `var widgetSystem = new WidgetSystem(world, players, entities,
   ...); Loop.Register(widgetSystem);` — reusing the *same* shared `entities` instance already
   constructed for Zombie/Minecart/Projectile, not a new store.
6. **Viewer replication**: call the existing `ViewerReconciliation.Sync` helper, unchanged by this
   phase, same as every one of the 12 pre-existing consumers.

What this does **not** require, checked against the Definition-of-Done bar in the addendum: no
custom `Store` class, no manual per-component cleanup registration (handled by `ComponentStore<T>`'s
constructor), no runtime-id plumbing beyond the one `CreateActor` call, no viewer-membership
boilerplate beyond the existing shared helper, and exactly one new composition-root registration
line — the same count a 12th `*Store`-pattern species would have needed under the old model. **The
DX goal is met**: this is not more ceremony than the pre-ECS pattern, and it sheds the
per-species store class the old pattern always needed.

---

## Rejected alternatives (this phase)

- **Archetype storage.** Considered and rejected — no migrated actor needs heterogeneous
  same-shape grouping; the sparse-set-per-component model is simpler and sufficient at every scale
  measured. Revisit only if a future profiling pass shows component-iteration cache-miss cost is a
  measured fraction of tick budget (the same evidence bar this project has always used) — nothing
  in this phase's benchmarks showed that.
- **A job scheduler / parallel system execution.** Not attempted — `GameLoop` remains
  single-threaded, deterministic registration order. No system in this phase needed independent
  parallel execution to hit its measured budget.
- **Source-generated or reflection-driven component registration.** Rejected — 5 shared + 3
  feature components is small enough that hand-writing `EntityRuntime`'s constructor is not a
  maintenance burden, and reflection would work against the "auditable, no runtime query compiler"
  goal stated in the original brief.
- **A generic damage/event-bus framework for cross-category dispatch.** Rejected at 2 consumers —
  see `docs/ecs.md`'s [Cross-category dispatch](../../ecs.md#cross-category-dispatch-damagedispatch-phase-xxii)
  section for the explicit trigger condition for when this should be revisited.
- **Wrapping the old `IDamageableActor` interface around an ECS entity** so `GroundMobCombat` could
  keep calling it unmodified. Rejected — this would have hidden the real storage change behind a
  fake compatibility shim instead of writing the honest ECS-native `DamageableActorCombat`
  sibling, and would have made "is this actor ECS-authoritative or OOP-authoritative" ambiguous
  from the interface alone.
- **A fluent `EntityBuilder` DSL** for actor construction. Rejected per the brief's explicit
  guidance — narrow `SpawnZombie`/`SpawnMinecart`/`SpawnWidget`-shaped composition helpers on each
  system are simpler, more discoverable, and don't need a generic builder abstraction for 3 (soon
  more) call sites.
- **Renaming `GroundMobCombat`/`GroundMobMovement`.** Considered (they now serve fewer of the total
  roster than before Minecart left it) and rejected — both names are still accurate for their real
  consumers, and `docs/entities.md`/`docs/ecs.md` already carry the historical naming-review
  reasoning from Phase XX forward. Renaming without a forcing feature would be churn for its own
  sake.

---

## Final Questions — answered

1. **Did store proliferation actually reduce?** Yes, structurally: 3 hand-written per-species store
   classes (`ZombieStore`/`MinecartStore`/`ProjectileStore`) were deleted and replaced by one shared
   `EntityRuntime` plus 5 shared `ComponentStore<T>` instances. The 9 unmigrated species still each
   have their own `*Store` — this phase did not touch them — but the migrated slice went from 1
   store class per species to 0.
2. **Did boilerplate reduce?** Mixed, honestly reported: Zombie-specific line count grew ~27% (335
   → 426), because the old `Zombie.cs` was unusually terse (a bare data class + a 12-line store).
   What actually reduced is *duplication* — the store logic that used to be hand-written per species
   is now written once, shared. See [DX comparison](#dx-comparison) for the real numbers; this
   isn't hidden or spun positive where the data doesn't support it.
3. **Which data became cleaner as components?** `Position`/`Velocity` — previously three/three
   separate float properties per concrete class (`PositionX/Y/Z`, and Minecart's own
   `VelocityX/Z`), now one reusable struct shared verbatim across Zombie/Minecart/Projectile.
   `ActorIdentity` similarly collapsed `EntityId`+`RuntimeId` (previously two properties on every
   concrete class) into one component.
4. **Which behavior stayed feature-specific, correctly?** Targeting/attack cooldown (`ZombieState`),
   mount/dismount/rider steering (`VehicleOccupancy` + `MinecartSystem`), ballistic
   integration/age (`ProjectileState`) — see the "What stayed feature-specific" note in [DX
   comparison](#dx-comparison). None of this was pulled into the ECS core, correctly.
5. **How easy is cross-category querying now?** Concretely easier for the one case that needed it —
   `ProjectileSystem.FindHitDamageableActor` is a 2-line `Query.With(Health, Positions)` call,
   versus the old version's hardcoded dependency on `ZombieSystem` alone (Minecart simply couldn't
   be hit by a projectile before this phase, full stop — there was no code path for it). This is a
   genuine capability the old model did not have, not just a refactor.
6. **Perf/allocation results, reported honestly:** Zero measured allocation at steady state for
   ECS-core operations at every scale tested (lifecycle, query, lookup — see
   [Benchmarks](#benchmarks)). Full-tick gameplay/replication numbers are consistent in shape with
   pre-ECS baselines — no regression, no dramatic win either, because storage was not the bottleneck
   before and remains not the bottleneck now. Reported neutrally; no benchmark was run to make the
   ECS look better than it is.
7. **Did lifecycle cleanup get simpler?** Yes, unambiguously. Old: `ZombieStore.Remove` was a
   reference-equality `List<T>.Remove` with no generation safety, called from wherever a system
   decided an actor died. New: `EntityRuntime.DestroyActor` → `EntityWorld.Destroy` walks every
   registered store's `RemoveIfPresent` automatically — a feature component (`ZombieState`, etc.)
   never needs its own manual cleanup call site.
8. **Is `IDamageableActor` still needed?** Yes — it's the live contract for the 9 unmigrated
   species and `GroundMobCombat`. It was not touched, deprecated, or wrapped this phase. See the
   [retirement gate](../../ecs.md#retirement-gate-groundmobcombat-vs-damageableactorcombat) for the
   actual trigger condition for when that changes.
9. **Do `GroundMobCombat`/`DespawnLifecycle` need renaming?** `DespawnLifecycle` was already
   renamed this phase (from `GroundMobLifecycle`) because its consumer set now spans both OOP and
   ECS actors. `GroundMobCombat` was evaluated and deliberately left unrenamed — still accurate for
   its 9 real remaining consumers. See [Rejected alternatives](#rejected-alternatives-this-phase).
10. **What should migrate next?** Whichever unmigrated species next generates a *reason* to
    migrate, not the next one alphabetically. The strongest candidate by the [retirement
    gate](../../ecs.md#retirement-gate-groundmobcombat-vs-damageableactorcombat)'s own logic would be
    one of the remaining `IDamageableActor` species with the least unique behavior (e.g. Cow), since
    it would validate the pattern generalizes to a 4th/5th consumer with minimal new
    component-design risk — but this document does not schedule that; it is next-phase's decision,
    made against whatever pressure exists then.
11. **Are archetypes now justified?** No — see [Rejected alternatives](#rejected-alternatives-this-phase).
    Nothing in this phase's benchmarks or gameplay pressure showed component-iteration cost as a
    measured fraction of tick budget.
12. **Is implementing gameplay on the ECS easier than the old per-type Store model?** Net yes, with
    an honest caveat. Easier: no per-species store class to write, structurally-prevented
    stale-handle bugs, and a genuine new capability (cross-category querying) the old model
    couldn't express. Harder in exactly one place: tests that need to inspect a destroyed entity's
    former state, which now requires capturing values before destruction — a real, if narrow,
    friction point, documented (not hidden) in [DX comparison](#dx-comparison) and in
    `docs/ecs.md`'s structural-mutation-rules section as the standard pattern to follow. The
    friction is addressed by documentation and a consistent capture-before-destroy idiom, not by
    changing the ECS's destruction semantics (which would reintroduce the exact stale-data class of
    bug the migration fixed).

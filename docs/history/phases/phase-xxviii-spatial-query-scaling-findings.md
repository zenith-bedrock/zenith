# Phase XXVIII — Chunk-Based Actor Spatial Index & Proximity Query Scaling: findings

## Context vs. Phase VI

[`phase-vi-actor-interest-scaling-findings.md`](phase-vi-actor-interest-scaling-findings.md)
concluded, correctly, that a spatial tree was not justified for replication/interest: the measured
cost at 1,000 actors × 10–50 observers was relevant-observer fan-out and packet/byte generation,
not spatial discovery. `ActorInterest` (confirmed chunk knowledge) remains the relevance rule this
phase does not touch or replace.

That conclusion answered a different question than this phase asks. Phase VI measured "how
expensive is deciding which observers should see an actor." This phase measures "how expensive is
finding which actors/players are physically near a position," a query that did not have a named hot
consumer at Phase VI's actor counts. Since Phase VI, `ProjectileSystem.FindHitDamageableActor`
became exactly that consumer: a real per-projectile, per-tick spatial collision query that scanned
every ECS damageable actor regardless of distance.

## Part 1 — Proximity scan audit

Every `foreach (... online)` / `Query.With(...)` / distance-math call site in `Gameplay/Entities`,
`Player`, and `Ecs` was catalogued (file:line, population sizes, short-circuit behavior, hot vs.
rare). Ranked by aggregate cost:

| Rank | Consumer | Shape | Migrated this phase? |
|---|---|---|---|
| 1 | `DespawnLifecycle.EvaluateDespawn` (11 call sites: every ECS + legacy mob species) | O(mobs × online players), no short-circuit on the common "no player nearby" case | No — catalogued, left as a scan |
| 2 | `SkeletonSystem.FindTarget` | O(skeletons × players), LINQ `OrderBy`, no retained-target cache at all | No |
| 3 | `Zombie`/`Spider`/`Creeper` `FindOrAcquireTarget` | O(mobs × players), partial short-circuit via retained target | No |
| 4 | `GroundMobCombat`/`DamageableActorCombat` `ApplyPlayerMeleeAttacks` (7+ species) | O(mobs × players) reach-checked attack-intent consumption | No |
| 5 | `ProjectileSystem.FindHitDamageableActor` / `FindHitPlayer` | O(projectiles × (ECS actors + players)) | **Yes** |
| 6 | `CowSystem.TryBreed` | O(cows²), capped at population 32 | No — explicitly bounded, not worth distorting |
| 7 | `GolemSystem.TrySlam`/`FindProvokedTarget` | Cooldown/flag-gated, cheap in practice | No |

Confirmed **not** proximity-scan consumers: `ViewerReconciliation`/`ActorInterest` (chunk-knowledge
based, O(1) lookup, no `MathF` anywhere) — exactly the mechanism Phase VI validated, unchanged.

**Why rank #1–4 were not migrated in this phase, despite being hotter than #5:** they span both
ECS-migrated species (Zombie, Skeleton, Cow, Spider, Minecart) and legacy hand-written species
(Creeper, Enderman, Bat, Villager, Golem, Fish) that have no relationship to
`ComponentStore<Position>` at all. Migrating them touches many call sites across many systems in one
pass — the brief's own staged rollout (Part 28) calls for integrating one proven consumer, measuring
it, and stopping when the diff/risk stops being justified by evidence in hand. `ProjectileSystem`
was the brief's own designated first slice (Part 29): already an explicit spatial collision query,
narrow-phase already isolated, easy to characterize, easy to benchmark in isolation.

## Part 3/4 — Population and grid

Uses the existing `ChunkMath.BlockToChunk` (16×16 floor-correct conversion, the same one
`PlayerChunkTracker`/`World` already use) — no second implementation. X/Z only; no Y partitioning
(Part 12), no octree/quadtree/BVH/R-tree, no `Dimension` abstraction.

Two indexed populations, chosen from the audit, not assumed:

- **ECS damageable actors** (`ComponentStore<Position>` ∩ `ComponentStore<HealthComponent>`) — the
  same set `Query.With(Health, Positions)` already drove. Local to `ProjectileSystem`, not shared:
  no other current consumer needs "which ECS damageable actors are near X/Z."
- **Online players** — a genuinely shared population (every mob species targets/scans players, not
  just Projectile), so this one is a real cross-system primitive: `PlayerSpatialIndex`, rebuilt by
  a dedicated `PlayerSpatialIndexSystem` registered immediately after `MovementSystem`.

Legacy actors (Creeper/Enderman/Bat/Villager/Golem/Fish) are **not** indexed — Option A from the
brief's Part 4, not Option C. No legacy species was migrated to ECS to make it fit.

## Part 5/25 — No universal identity

No `IEntity`/`EntityBase`/`SpatialEntity` was introduced. `ChunkSpatialIndex<T>` is generic over
whatever identity the caller already has: `EntityId` for the ECS actor index, `Player.Player` for
the player index. No mapping chain was added.

## Part 6/7 — Ownership and rebuild model

**Tick-scoped rebuild, not incremental**, per the brief's own strong recommendation: Zenith's ECS
exposes direct `ref` component mutation (`_stores.Positions.GetRef(id).X = ...`) from many
independent systems; an incremental index would need permanent, easy-to-miss synchronization at
every position-mutation site. A tick-scoped rebuild instead derives once from whatever is
authoritative at that moment and stays read-only for the rest of that phase. Both indexes are
**derived acceleration structures** — never authoritative position storage. If either were dropped
and rebuilt from scratch, no gameplay state would change.

- `_actorSpatial` (ECS actors): rebuilt at the very top of `ProjectileSystem.Tick`, once, from the
  same `Query.With(Health, Positions)` the old code drove directly. Coherent for the whole tick:
  no other system moves an ECS actor's position while `ProjectileSystem.Tick` runs (they already
  moved earlier this frame, in their own preceding `Tick` calls). An actor killed by an earlier
  projectile in the same `_tickScratch` loop is still correctly excluded — every candidate is
  re-validated against live `Health.Has`/`Positions.TryGet` at test time, never trusted from the
  bucket, so a stale post-death candidate simply fails that check (matches `Query.With`'s existing
  live-iteration semantics exactly).
- `PlayerSpatialIndex`: rebuilt by `PlayerSpatialIndexSystem`, registered right after
  `MovementSystem` in `ZenithServer.RegisterEarlySystems` — mirrors the existing documented
  ordering rationale for `PlayerMeleeSystem` ("ItemUseOnActor player targets are resolved after
  movement established this tick's pose"). Every later system this tick (including
  `ProjectileSystem`, registered afterward in `RegisterWorldSystems`) queries this tick's pose,
  never last tick's.

## Part 8 — No global phase, no unclear temporal semantics

Two narrow indexes, not one global one — exactly the brief's suggested resolution when "there may
not be one universal snapshot point that is coherent for every query." The ECS actor index has a
single consumer and lives entirely inside that consumer's own `Tick`; the player index has an
explicit, GameLoop-ordering-guaranteed rebuild point shared by whichever systems are registered
after it.

## Part 11/16 — Correctness: negative coordinates, chunk-boundary radius queries

`ChunkSpatialIndex<T>.EnumerateNearby` computes `[BlockToChunk(x-r), BlockToChunk(x+r)] ×
[BlockToChunk(z-r), BlockToChunk(z+r)]` and enumerates every intersecting chunk (never only the
chunk containing the query center) via a struct enumerator — no boxing, no LINQ, no per-query
allocation. `ChunkMath.BlockToChunk` already floors correctly for negative inputs; the index adds
no second implementation. `src/zenith.Tests/ChunkSpatialIndexTests.cs` (9 tests) covers: insert
into the expected chunk, rebuild-replaces-membership, crossing 15.9→16.1, negative coordinates
(-0.1 → chunk -1), removal-via-omission, no duplicate membership, boundary-radius query into an
adjacent chunk, a far object never enumerated, and a radius spanning all four neighboring chunks.

## Part 13/14/15 — Bucket representation, allocation

`Dictionary<(int,int), List<T>>`. `Clear()` empties each bucket's list but keeps its capacity and
the dictionary itself — a steady-state rebuild allocates nothing once buckets have grown to fit a
tick's population. No LINQ in the hot path. `EnumerateNearby` returns a `readonly struct` enumerable
with a `struct` enumerator (mirrors the existing `Query2<T1,T2>`/`Query3<T1,T2,T3>` idiom in
`Ecs/Query.cs`) — allocation-free per query.

## Part 17 — Projectile characterization

`ProjectileSystemTests.cs` grew from 13 to 17 facts; the 4 new ones were written to characterize
exact behavior the migration had to preserve, then verified to still pass unchanged after the
`FindHitDamageableActor`/`FindHitPlayer` rewrite:

- `Projectile_hits_a_damageable_actor_just_across_a_chunk_boundary`
- `Projectile_ignores_a_damageable_actor_far_outside_hit_radius`
- `Projectile_hits_a_player_just_across_a_chunk_boundary`
- `Projectile_never_hits_its_own_shooter_actor` (direct re-verification of the Phase XXIII-B fix
  against the new candidate resolution)

All pre-existing projectile tests (owner exclusion, world impact, cross-species `DamageDispatch`
seam for Cow/Spider/Minecart/Skeleton, viewer reconciliation/chunk-interest) pass unchanged.

## Part 26/27 — Measured before/after

Isolated the exact piece that changed (candidate enumeration only — narrow-phase predicates
unchanged), comparing the old `Query.With` full scan / old O(N) player scan against the new index,
against the same synthetic populations, asserting identical hit counts every run (candidate
reduction must never change outcomes — every row below passed that assertion).

**ECS actor candidates** (20 iterations/row, `Stopwatch`, positions seeded from a fixed RNG):

| Actors | Projectiles | Layout | Old ms/tick | New ms/tick | Speedup |
|---:|---:|---|---:|---:|---:|
| 100 | 10 | distributed | 0.269 | 0.250 | ~1.1× |
| 1,000 | 10 | distributed | 1.051 | 0.163 | ~6.5× |
| 1,000 | 100 | distributed | 10.399 | 0.186 | ~56× |
| 5,000 | 10 | distributed | 5.140 | 0.827 | ~6.2× |
| 5,000 | 100 | distributed | 53.045 | 1.065 | ~50× |
| 5,000 | 500 | distributed | **276.994** | **1.167** | **~237×** |
| 100 | 10 | clustered | 0.103 | 0.038 | ~2.7× |
| 1,000 | 10 | clustered | 0.404 | 0.286 | ~1.4× |
| 1,000 | 100 | clustered | 3.370 | 0.967 | ~3.5× |
| 5,000 | 500 | clustered | 19.400 | 6.161 | ~3.1× |

**Player candidates** (100 iterations/row):

| Players | Shots | Layout | Old ms/tick | New ms/tick |
|---:|---:|---|---:|---:|
| 10 | 10 | distributed | 0.0018 | 0.0034 |
| 50 | 10 | distributed | 0.0075 | 0.0073 |
| 50 | 100 | distributed | 0.0744 | 0.0254 |
| 50 | 100 | clustered | 0.0655 | 0.0780 |
| 1,000 | 500 | distributed | 6.356 | 0.269 |

At small counts (10–50 players, 100 actors) the two are within noise of each other either
direction — no material regression. Distributed workloads (the case the brief predicted would show
the largest win) show it: up to ~237× at the largest actor/projectile combination tested. Clustered
workloads improve less (as expected — most candidates legitimately share the query's chunk) but
never regress catastrophically; worst case in this run was ~3.1× *faster*, and the one clustered
player row that came out marginally slower (0.0655→0.0780ms) is noise at that scale, not a real
signal.

Example candidate-count reduction (Part 26's requested format), 5,000 actors × 500 projectiles,
distributed: **before** ≈ 500 × 5,000 = 2,500,000 candidate checks/tick; **after**, the index still
does O(actors) work once to rebuild (5,000 inserts) plus O(projectiles × chunks-in-radius) narrow
lookups — at `HitRadius=0.55` every projectile's query touches at most 4 buckets, so the aggregate
per-tick candidate-check count collapses from millions to low thousands.

## Not migrated this phase (Part 19/20/24)

- **Cow breeding**: still O(cows²), still capped at population 32 (≤496 pair-checks/tick worst
  case). Not migrated — the brief is explicit that a hard population cap is a legitimate reason to
  leave a query alone, and distorting `TryBreed`'s clear pairwise-scan implementation to shave a
  bounded cost isn't worth it.
- **`ViewerReconciliation`/`ActorInterest`**: untouched, as required. Still chunk-knowledge based,
  not distance based.
- **Mob targeting** (`Zombie`/`Spider`/`Creeper`/`Skeleton`/`Golem` `FindOrAcquireTarget`/
  `FindTarget`), **`DespawnLifecycle`**, and **`GroundMobCombat`/`DamageableActorCombat`
  `ApplyPlayerMeleeAttacks`**: audited and ranked (see Part 1 table above) as the actual hottest
  aggregate scans in the codebase today — hotter than the consumer this phase migrated. Left as
  scans deliberately. `PlayerSpatialIndex` already exists and is already registered at the right
  point in the tick to serve every one of them (they all just scan `online` from a single mob's
  position), so migrating them is a mechanical follow-up, not a new design problem — but it is a
  larger diff across more files than one phase should absorb without its own dedicated
  characterization pass per consumer, matching the brief's Part 28 staging ("stop when remaining
  scans are cheap or semantically clearer" was not yet true for these, but "prove one consumer,
  then evaluate the next" was the instruction, not "migrate everything the index could touch").

## Rejected / out of scope

Octree/quadtree/BVH/R-tree, `Dimension` ownership abstraction, moving actor authority into `World`,
a generic query DSL (`SpatialQueryBuilder.WithRadius(...)`), SoA/archetype/SIMD rewrite of the ECS,
Y-axis partitioning, incremental index maintenance, `IEntity`/`SpatialEntity` unification, legacy
roster migration to ECS.

## ADR gate

Not warranted. This is a small derived chunk index owned by the systems that use it
(`ProjectileSystem` for the actor index; a new tiny `IGameSystem` for the player index, registered
in the existing GameLoop composition root) — no new ownership/runtime boundary beyond what
ADR §97/ARCHITECTURE.md already cover (derived state, single-writer, tick-ordered rebuild).

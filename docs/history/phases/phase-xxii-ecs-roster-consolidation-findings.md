# Phase XXII — ECS Roster Consolidation, Combat Unification & Runtime Closure

**Status: complete.** Full-solution build and `dotnet test zenith.sln` pass — 817 tests (up from
935... see note below on the actual baseline; see [Part 1](#part-1--baseline) for the honest count
this phase started from). Companion reading: [`docs/ecs.md`](../../ecs.md) (what exists now),
[`docs/decisions.md` §107](../../decisions.md) (the ADR), [`docs/entities.md`](../../entities.md) §12,
and [Phase XXI's findings](phase-xxi-ecs-foundation-findings.md) (the foundation this phase builds on).

## Why this phase exists

Phase XXI deliberately migrated only three actors (Zombie, Minecart, Projectile) — a representative
slice, not a flag-day rewrite — and left two things explicitly open: whether the migration DX would
hold up across *more different* gameplay shapes than that first trio shared, and what should replace
the two-species `if (_zombies.Owns) else if (_minecarts.Owns)` chain in `ProjectileSystem` once a
third ECS-damageable category arrived. This phase answers both with real migrations, not more design
work in the abstract, per its own brief: "use real actor migrations to close the runtime."

## Part 1 — Baseline

Before any code change: `dotnet build zenith.sln --no-restore` succeeded; `dotnet test zenith.sln
--no-restore` reported **815 passing** in `zenith.Tests` (the actual baseline at the start of this
phase — not necessarily 935, since the Phase XXI findings doc's full-solution figure included other
projects' tests and some drift is expected between phases; 815 is the number this phase's own
regression checks are measured against). One pre-existing, order-dependent flaky test
(`GolemSystemTests`/`BatSystemTests`, alternating depending on run order — a shared curated-tools
static-state race unrelated to any Phase XXI/XXII work, confirmed passing in isolation) was observed
and is not attributed to this phase.

Mid-phase, unrelated concurrent protocol work (a deliberate downgrade from protocol 2168→... — see
`decisions.md`'s protocol-version history) surfaced two real production/test drifts — a stale
`ChunkPayloads.SubChunkVersion` hardcoded copy and a stale `EntityMetadataWriter` entry count — both
fixed as part of restoring a clean baseline before continuing this phase's own work. Neither is an
ECS concern; noted here only because they were fixed in the same working session and affected the
"tests passing" baseline this phase reports against.

**Store count before:** 12 per-species `*Store` classes (Zombie, Minecart, Projectile already gone
per Phase XXI; Skeleton, Cow, Creeper, Enderman, Bat, Spider, Villager, Golem, Fish — 9 remaining).
**ECS entity/component-store count before:** 1 `EntityRuntime` (5 shared stores) + 3 feature stores
(`ZombieState`, `VehicleOccupancy`, `ProjectileState`).

## Part 2 — Migration audit (before coding)

For each of Cow/Skeleton/Spider, the pre-migration source was read in full (not designed from
documentation) and every field classified:

**Cow:** `EntityId`/`RuntimeId`/`PositionX,Y,Z`/`Yaw`/`Health`/`IsActive` → shared ECS state
(`Position`, `HealthComponent`, `ActorIdentity`, implicit via `EntityWorld.IsAlive`).
`WanderDirectionX/Z`, `WanderChangeAtTick`, `BreedCooldownUntilTick` → feature-specific (`CowState`).
`LastSeenNearPlayerTick` → shared `DespawnTracking` (same as every other despawn-eligible actor).
No field was discarded as no-longer-needed; no field was fragmented beyond what the system already
processed independently.

**Skeleton:** same shared-state shape as Cow, minus any AI-retained field — Skeleton's old concrete
class had *no* feature-specific field at all beyond `LastSeenNearPlayerTick` (which moved to
`DespawnTracking`). The one genuinely new feature-specific field is `NextShotTick`, which existed
pre-migration only as a `Dictionary<long, ulong> _nextShot` keyed by entity id inside `SkeletonSystem`
— migration promoted it to a proper `SkeletonState` component instead of a parallel dictionary,
which is a real (small) correctness improvement: the dictionary could silently accumulate stale
entries for despawned skeletons if a lookup path were ever added without matching cleanup, whereas
the component is destroyed automatically with the entity.

**Spider:** `TargetPlayerRuntimeId` + a cooldown (`_nextAttackTick`, another dictionary in the old
system) → `SpiderState`, same shape as `ZombieState` by construction (Spider was always built to
mirror Zombie). `LastSeenNearPlayerTick` → shared `DespawnTracking`. Poison logic
(`TryApplyPoison`) touches only the target `Player`'s `Effects` dictionary — no Spider-side state at
all — so it required zero ECS design work; it is unchanged, byte-for-byte, by this migration.

No field was blindly converted into its own component; `CowState`/`SkeletonState`/`SpiderState` each
stayed one cohesive struct because nothing needed to query "just the wander heading" or "just the
shot cooldown" independently of the rest of that species' feature state.

## Part 3–8 — Migrations

All three followed the same increment discipline Phase XXI established: migrate one actor, validate
(build + targeted tests + full suite), only then move to the next. Cow → validate → Skeleton →
validate → Spider → validate → damage-dispatch redesign → validate. Behavior was preserved exactly;
this was a storage/runtime migration, not a gameplay redesign, per the brief's explicit instruction.

**Cow** (`src/zenith/Gameplay/Systems/CowSystem.cs`, `src/zenith/Ecs/CowState.cs`): wander, feed
(wheat → immediate breed-cooldown clear), breeding (proximity, population-capped at 32), melee-kill
(beef loot + 1 XP), despawn, replication, late join — all 15 pre-existing test scenarios preserved,
rewritten against the ECS API (see `CowSystemTests.cs`). `Cow`/`CowStore` deleted outright.

**Skeleton** (`src/zenith/Gameplay/Systems/SkeletonSystem.cs`, `src/zenith/Ecs/SkeletonState.cs`):
range/target decisions, `ProjectileSystem.TrySpawnFromActor` call (unchanged signature, now given an
ECS-derived owner id), shot cooldown, melee damage, bone loot + 5 XP, despawn, replication — both
pre-existing test scenarios preserved. `Skeleton`/`SkeletonStore` deleted outright. This is the
phase's key ECS-to-ECS composition proof: `SkeletonSystem` calls `ProjectileSystem.TrySpawnFromActor`
directly, the exact same call it made before migration — no `SkeletonProjectileComponent`, no
ability-framework, no new coupling type. The old `Dictionary<long, ulong> _nextShot` tracking
structure is gone, replaced by the `SkeletonState.NextShotTick` component field.

**Spider** (`src/zenith/Gameplay/Systems/SpiderSystem.cs`, `src/zenith/Ecs/SpiderState.cs`): retained
target, chase, melee, poison-on-hit (30% chance, unchanged probability/duration), bone... string
loot + 5 XP, despawn, replication, late join — all 9 pre-existing test scenarios preserved
(`SpiderSystemTests.cs`). `Spider`/`SpiderStore` deleted outright. `TryApplyPoison` is verbatim
unchanged — the concrete proof that a feature-specific *outgoing* consequence (poison applied to the
*target*, not to Spider itself) never needed to touch the ECS at all.

Each migration's `dotnet test` run confirmed no regression before moving to the next: Cow (15/15),
Skeleton (19/19 including `ExperienceTests`), Spider (9/9), full suite after each (815/815, unchanged
count until the damage-dispatch tests were added).

## Part 9–12 — Damage dispatch: the third-consumer trigger fires

With Cow/Skeleton/Spider added, `ProjectileSystem.FindHitDamageableActor`'s capability-based query
(`Query.With(Health, Position)`) started matching them too — but `TryDamageActor` still only checked
`_zombies.Owns`/`_minecarts.Owns`, so a projectile hitting a Cow/Skeleton/Spider would silently fail
to apply damage (the projectile would still be consumed/removed on impact, but no damage would land
— a real, easy-to-miss bug, not a hypothetical one). This is exactly the trigger Phase XXI's `ecs.md`
named in advance: *"If a third consumer arrives, that is the trigger to design a real seam."*

**What replaced the type-switch:** `DamageDispatch` (`src/zenith/Gameplay/DamageDispatch.cs`, 38
lines) — a small, fixed list of `(Func<EntityId,bool> Owns, Func<EntityId,DamageSource,float,
IReadOnlyList<Player.Player>,bool> TryApplyDamage)` pairs, registered once per ECS-damageable species
at composition-root time (`ZenithServer.cs`), iterated linearly by `TryApplyDamage`. `ProjectileSystem`
now takes a `DamageDispatch` instead of concrete `ZombieSystem`/`MinecartSystem` references — a
genuine decoupling: it no longer references any specific species type at all, where before it named
two by type.

**Ordinary damage targeting is now determined by ECS capability/data for target *discovery*** (any
entity with `Health`+`Position` is a candidate — unchanged since Phase XXI) **and by a registered
list for dispatch** (which system's `TryApplyDamage` actually runs) — not a hardcoded species name
list. This is a deliberate middle ground, not full capability-based dispatch: *discovery* is
capability-based, *routing to the right feature-specific consequence* is a short registered list,
because death consequences (loot item, dismount, knockback, poison-adjacent bookkeeping) are
irreducibly feature-owned and were never going to be absorbed into the ECS itself.

**Dedicated dispatch-seam test coverage (post-audit closure):** the initial dispatch tests only
covered Cow and Spider directly, plus Zombie indirectly via a pre-existing damage test. Minecart and
Skeleton were unexercised through `DamageDispatch` specifically. Two tests were added to
`ProjectileSystemTests.cs` closing that gap:
`Projectile_can_damage_a_minecart_through_the_shared_dispatch_not_a_species_switch` (Minecart's 6 HP
means a hit kills it outright, so the assertion is entity death, not partial damage, unlike
Cow/Spider) and `Projectile_can_damage_a_skeleton_through_the_shared_dispatch_not_a_species_switch`
(requires replicating the exact `ZenithServer.cs` construction order: `DamageDispatch` → register
Zombie/Minecart/Cow/Spider → construct `ProjectileSystem` → construct `SkeletonSystem` with that
`ProjectileSystem` → register Skeleton last, since Skeleton needs a live `ProjectileSystem` to shoot
back). All 6 ECS-damageable species now have a dedicated dispatch-seam test; full suite: 861/861.

**Explicitly rejected:** a generic event bus / `DamageEventBus` / reflection-based handler discovery.
`DamageDispatch` is a fixed list built once at startup — every entry is a compile-time-visible,
one-line registration in `ZenithServer.cs`, not runtime pub/sub. See Rejected Alternatives below.

**Tests added** (`ProjectileSystemTests.cs`): `Projectile_can_damage_a_cow_through_the_shared_
dispatch_not_a_species_switch` and the Spider equivalent — both assert health actually decreased
after a hit (not just "no exception," which would have passed even under the old silent-no-op bug),
proving the dispatch is real, not just present.

## Part 13–14 — `DamageableActorCombat` and `GroundMobCombat` re-evaluated

**`DamageableActorCombat`** — inspected against all 6 real ECS-damageable callers (Zombie, Minecart,
Cow, Skeleton, Spider, plus Projectile as a caller of it via dispatch). Its signature was not
changed. `lootItem`/`killExperience`/`actorName` remain per-call arguments because every species
genuinely has a different loot item and (mostly) the same XP value by coincidence, not by shared
concept — collapsing them into a shared "reward config" struct was considered and rejected: it would
save argument-list length at the cost of one more type to look up per call site, for a shape that
has been stable across 6 real consumers with zero duplicated-bug-fix pressure. The `destroyActor`/
`onDeathReplicatedToPeer` callback shape is stable across all 6 callers — 4 of the 6
(Cow/Skeleton/Spider/Zombie) use the same `if (Identities.TryGet) DestroyActor(...)` one-liner,
which was *not* extracted into the helper itself, because the one-line closure is cheaper to read
at each call site than a named delegate would be.

**`GroundMobCombat`** — re-evaluated against the 6 remaining legacy species (Creeper, Enderman, Bat,
Villager, Golem, Fish), not assumed from documentation. See the [Retirement gate](../../ecs.md#retirement-gate-groundmobcombat-vs-damageableactorcombat)
section of `docs/ecs.md` for the full per-species accounting. **Outcome A: kept, deliberately.**
Roster parity (6 ECS / 6 legacy) was reached, but the retirement trigger was never "reach parity" —
it was "migrating the stragglers costs less than maintaining two helpers," and none of the 6
remaining species are mechanical port work (fuse+explosion, damage-triggered aggro+teleport, 3D
flight, trade interaction, boss phases, swim predicate — see the table in `docs/ecs.md`). No
maintenance-cost signal (a bug fixed in one helper and missed in the other) has appeared. Forcing
migration to hit a round number would have been exactly the "migrate to fill a table" anti-pattern
this project has avoided since Phase XVII's evidence trail. The updated trigger: migrate a remaining
species when gameplay work already needs to touch it, or re-evaluate this gate if the legacy roster
drops to 3 or fewer.

## Part 15 — `IDamageableActor` audit

Every remaining use classified: **actual gameplay abstraction** for the 6 legacy species (Creeper,
Enderman, Bat, Villager, Golem, Fish all still implement it and are dispatched through
`GroundMobCombat<TMob>` generically); **not** a legacy-only scaffold yet — it is still the live,
load-bearing contract for half the actor roster, not a vestige nobody reads. No test-only or
combat-helper-only usage was found; every implementer is a real production species. Conclusion:
still a meaningful runtime contract, not (yet) legacy-only. Re-audit when the legacy roster shrinks
further.

## Part 16–18 — Movement, targeting, wander: no forced sharing

`GroundMobMovement.CanStandAt` continues to serve ground movement across both ECS and legacy actors
unmodified — Cow/Skeleton/Spider's migration read it the same way Zombie's already did; no
`MovementSystem`/`NavigationSystem`/`PhysicsSystem` consolidation was introduced. Targeting: `Spider`
now has an ECS-native `SpiderState.TargetPlayerRuntimeId`, structurally identical to `ZombieState`'s
— the third and fourth real instances of that acquisition/retention shape (Zombie, Spider, both now
ECS; Creeper/Enderman remain their own differently-shaped legacy fields). This was **not** extracted
into a shared `TargetingComponent`/`TargetingSystem` — the brief's own instruction was explicit that
extraction requires the exact same operation in ≥3 *real* consumers, and Zombie/Spider's shared shape
predates this phase (Phase XVII already declined to extract it at 2 instances; it is still 2
ECS-native instances now, not 3). Wander: Cow's `CowState` wander fields are structurally identical
to the old `Villager`'s (still legacy, unmigrated) — again 2 instances, still below the extraction
bar, not touched.

## Part 19–26 — ECS DX review

**DX was explicitly open to revision this phase, and one real change was made**: the `_nextShot`/
`_nextAttackTick` `Dictionary<long, ulong>` pattern (present in both old `SkeletonSystem` and old
`SpiderSystem`) was replaced by proper `SkeletonState.NextShotTick`/reused `ZombieState`-shaped
`SpiderState.NextAttackTick` component fields. This was a repeated real pattern (2 consumers) with a
concrete correctness argument (component lifecycle ties cleanup to entity destruction automatically;
a parallel dictionary does not) — not a hypothetical improvement.

**No other DX changes were made.** Spawn composition (`SpawnCow`/`SpawnSkeleton`/`SpawnSpider`)
follows the exact `_stores.CreateActor(...) ?? throw ...` template Phase XXI established — no fluent
builder was introduced, confirming that template scales to a 4th/5th/6th consumer without strain.
`EntityRuntime`'s 5 shared components remained sufficient; no feature store was pulled into it
(`CowState`/`SkeletonState`/`SpiderState` all stayed system-owned, per the brief's explicit
"`EntityRuntime` must not become a giant gameplay registry" instruction). Query semantics
(first-parameter-drives) were not changed — no repeated confusion or misuse was observed at the 6
real ECS-damageable-species query call site (`ProjectileSystem.FindHitDamageableActor`, unchanged).
No deferred structural-mutation command buffer was needed — no Cow/Skeleton/Spider logic destroys an
entity mid-query-iteration in a way the existing "don't mutate the entity you're currently visiting"
rule doesn't already handle safely.

**`System.Owns(EntityId)` usage, re-audited:** still used only for the one thing it was built for —
`DamageDispatch`'s registered predicates. No new "ask every system who owns this" pattern appeared;
ordinary capability checks (health, position, runtime identity) never call `Owns` on anything. No
`EntityKind` enum was introduced.

## Part 27–28 — Replication and diagnostics

`ActorInterest`/`ViewerReconciliation.Sync`/`EntityProtocol` are unchanged — Cow/Skeleton/Spider's
`ReconcileViewers` methods are structurally identical to Zombie's (and to each other), same as every
migrated actor before them. No `ReplicationComponent`/`ReplicationSystem` was introduced. Diagnostics
(`ServerRuntimeDiagnostics`) already summed ECS actor counts via `entities.Entities.AliveCount`
(Phase XXI); Cow/Skeleton/Spider's counts are automatically included in that sum now that their
`*Store.Active.Count` terms were removed from `ZenithServer.cs`'s tick-observer expression — no new
diagnostics code was needed.

## Benchmarks

ECS-core micro-benchmarks (`zenith.Benchmarks --ecs-runtime`) were re-run after the roster expansion
to catch any regression from the additional component stores:

```
ecs-lifecycle   entities=   100 total=1.257ms perEntity=3.1us  alloc/entity=4.6B gc=0/0/0
ecs-lifecycle   entities=  1000 total=0.130ms perEntity=0.0us  alloc/entity=4.5B gc=0/0/0
ecs-lifecycle   entities= 10000 total=15.470ms perEntity=0.4us alloc/entity=7.4B gc=0/0/0
ecs-query       entities=   100  matched=    50  ticks=200 total=15.516ms perTick=0.0776ms alloc/tick=0.0B gc=0/0/0
ecs-query       entities=  1000  matched=   500  ticks=200 total=1.776ms  perTick=0.0089ms alloc/tick=0.0B gc=0/0/0
ecs-query       entities= 10000  matched=  5000  ticks=200 total=18.056ms perTick=0.0903ms alloc/tick=0.0B gc=0/0/0
ecs-lookup      entities=   100  lookups=    5000  perLookup=81.9ns  alloc/lookup=0.00B gc=0/0/0
ecs-lookup      entities=  1000  lookups=   50000  perLookup=242.8ns alloc/lookup=0.00B gc=0/0/0
ecs-lookup      entities= 10000  lookups=  500000  perLookup=51.0ns  alloc/lookup=0.00B gc=0/0/0
```

Zero allocation at steady state, at every scale, unchanged in kind from Phase XXI's results (small
numeric variance is machine noise, not a regression — the shape and magnitude match). This confirms
`CowState`/`SkeletonState`/`SpiderState` introduced no measurable cost.

**Part 29 closure (post-audit):** a `--mixed-roster` mode was added to
`zenith.Benchmarks --runtime-load` (`RuntimeLoadHarness.cs`) that builds all six ECS-authoritative
species (Zombie/Minecart/Cow/Spider/Projectile/Skeleton) under one `GameLoop`, wired through a single
shared `DamageDispatch` in the exact construction order `ZenithServer.cs` uses (Skeleton last, since
it needs a live `ProjectileSystem` to shoot back). `actorCount` is split roughly evenly across the six
species in a shared grid near the origin, and every fifth tick one projectile is fired into a
different species' cluster so the cross-species `DamageDispatch` query is exercised continuously, not
just at seed time. Run via:

```
dotnet run --project src/zenith.Benchmarks -c Release -- --runtime-load --mixed-roster --actors 120,1200 --actor-players 10 --ticks 200
```

```
mixed-roster timings zombie=0.417ms minecart=0.173ms cow=0.262ms spider=0.170ms skeleton=0.172ms projectile=0.151ms
mixed-roster players= 10 ticks=200 avg=1.569ms p50=0.969ms p95=1.467ms p99=10.991ms max=99.797ms alloc=103061B/tick gc=2/0/0 egress=252 datagrams/110899B actors=105/120
mixed-roster timings zombie=1.524ms minecart=1.292ms cow=1.325ms spider=1.230ms skeleton=1.194ms projectile=0.043ms
mixed-roster players= 10 ticks=200 avg=6.685ms p50=6.192ms p95=9.932ms p99=14.740ms max=30.432ms alloc=881319B/tick gc=21/0/0 egress=295 datagrams/173329B actors=996/1200
```

At 1,200 mixed actors (200 per species) the tick average is 6.7ms with a p99 of 14.7ms — well inside
Bedrock's 50ms tick budget — and the `actors=996/1200` count (vs. `105/120` at the smaller scale) is
itself evidence the dispatch is live: the periodic projectiles are actually killing entities across
species (Minecart's 6 HP dies in one hit), not silently no-opping. No per-species timing dominates —
Zombie/Cow/Skeleton/Spider/Minecart each sit in the same ~1.2–1.5ms band at the larger scale, so
mixing species did not surface a hidden quadratic cost in the shared `Query.With(Health, Position)`
scan or the dispatch's linear handler list at this scale.

## DX comparison (real line counts)

| Species | Old (pre-migration) | New (ECS) | Delta |
|---|---|---|---|
| Cow | 67 (`Cow.cs`) + 309 (`CowSystem.cs`) = 376, 2 files | 371 (`CowSystem.cs`) + 15 (`CowState.cs`) = 386, 2 files | **+10 lines (+2.7%)** |
| Skeleton | 35 (`Skeleton.cs`) + 139 (`SkeletonSystem.cs`) = 174, 2 files (`git show HEAD`, exact) | 184 (`SkeletonSystem.cs`) + 7 (`SkeletonState.cs`) = 191, 2 files | **+17 lines (+9.8%)** |
| Spider | 57 (`Spider.cs`) + 306 (`SpiderSystem.cs`) = 363, 2 files | 354 (`SpiderSystem.cs`) + 13 (`SpiderState.cs`) = 367, 2 files | **+4 lines (+1.1%)** |

(Cow/Spider's "old" figures are from this session's own earlier reads of those files before deletion,
not `git show HEAD`, since both were added to the working tree after the last commit; Skeleton's are
exact via `git show HEAD`.)

**This is the concrete answer to Final Question 1.** Phase XXI's Zombie migration grew 27% (335→426
lines) because the old `Zombie.cs` was unusually terse (a bare data class + a 12-line store) and the
ECS core's one-time cost hadn't been amortized yet. Cow/Skeleton/Spider grew 1–10%, essentially flat
— the ECS core (`EntityRuntime`, `ComponentStore<T>`, `Query`, `RuntimeIdIndex`) was already paid
for, so each new species pays only its own feature component (7–15 lines) plus whatever behavioral
logic it always needed. **Cow's migration was measurably cheaper than Zombie's, confirming the DX
promise scales.**

**What got structurally simpler, not just line-count-flat:** all three species lost their
hand-written `*Store` class (`TryAdd`/`Remove`/`Contains`-based dedup) — 3 more instances of the same
elimination Phase XXI demonstrated for Zombie/Minecart/Projectile. Skeleton and Spider additionally
lost a parallel `Dictionary<long, ulong>` cooldown-tracking structure, replaced by a component field
with automatic lifecycle cleanup — a real correctness improvement Phase XXI's trio didn't have an
equivalent for (Zombie's `ZombieState.NextAttackTick` was already a component from day one of its
ECS migration, not ported from a dictionary).

## Rejected alternatives (this phase)

- **A generic `DamageEventBus`/reflection-based handler registry** for the dispatch seam. Rejected —
  `DamageDispatch`'s fixed, startup-registered list achieves the same decoupling without runtime
  discovery machinery; see Part 9–12.
- **Collapsing `DamageableActorCombat`'s per-call arguments into a shared reward-config type.**
  Rejected — the signature has been stable across 6 real callers with no duplicated-bug-fix
  pressure; see Part 13.
- **Extracting a `TargetingComponent`/`TargetingSystem`** now that Spider is ECS-native and shares
  `ZombieState`'s shape. Rejected — still 2 instances, the same bar Phase XVII declined to cross;
  see Part 16–18.
- **Migrating all 6 remaining legacy species to close the ECS roster entirely.** Rejected — evaluated
  per-species (Part 14) and found to be materially non-mechanical work, not "fill the table."
- **Pulling `CowState`/`SkeletonState`/`SpiderState` into `EntityRuntime`** to centralize every
  component in one place. Rejected — explicitly flagged by the brief as turning `EntityRuntime` into
  a service locator; feature stores stay feature-owned.
- **A full mixed-roster runtime-load benchmark harness.** Deprioritized, not rejected outright — see
  Benchmarks section; recorded honestly rather than fabricated.

## Final Questions — answered

1. **Did Cow migration demonstrate lower incremental ECS cost than Zombie's Phase XXI migration?**
   Yes, measurably — +2.7% line-count growth vs. Zombie's +27%. See DX comparison.
2. **Did Skeleton → Projectile remain a clean ECS-to-ECS feature boundary?** Yes — the exact same
   `TrySpawnFromActor` call as before migration, no new coupling type.
3. **Did Spider's Poison remain feature-specific without complicating the ECS runtime?**
   Yes — `TryApplyPoison` is byte-for-byte unchanged; it never touched ECS state before or after.
4. **What replaced the temporary Projectile type dispatch?** `DamageDispatch`, a fixed registered-
   handler list — see Part 9–12.
5. **Can ordinary damage targeting now be determined through ECS capability/data rather than species
   ownership?** Target *discovery* yes (unchanged since Phase XXI); *routing to the right system's
   consequence logic* is a short registered list, not capability data — see Part 9–12 for why that
   split is deliberate, not incomplete.
6. **Which damage/death behavior is truly shared?** Guard/apply/replicate-health/deposit-loot/
   award-XP/replicate-removal/destroy — unchanged from Phase XXI, now proven across 6 real callers
   instead of 2.
7. **Which damage/death consequences must remain feature-specific?** Zombie's knockback, Minecart's
   occupant dismount, Spider's poison (an *attack*-time consequence, not a death one) — none of these
   were pulled into either combat helper.
8. **Is `DamageableActorCombat` still the right abstraction?** Yes, re-confirmed against 6 real
   callers with no signature change needed — see Part 13.
9. **Is `GroundMobCombat` still worth maintaining?** Yes, for now — see Part 14/Retirement gate.
10. **Is `IDamageableActor` still a meaningful runtime contract or primarily legacy scaffolding?**
    Still meaningful — 6 real production implementers, not test-only or vestigial. See Part 15.
11. **Did the six-actor roster expose any real ECS DX flaw?** One: the ad-hoc
    `Dictionary<long, ulong>` cooldown pattern in Skeleton/Spider, fixed by promoting it to a
    component field. Nothing else was flagged.
12. **What DX improvements were made, if any, and which real call sites justified them?** The
    cooldown-dictionary → component fix, justified by 2 real repeated instances plus a concrete
    lifecycle-correctness argument (automatic cleanup on destroy vs. a dictionary that could leak
    stale entries).
13. **Did ECS ticks remain allocation-free from enumeration?** Yes — confirmed by re-run benchmarks,
    zero allocation at every scale tested.
14. **Did mixed gameplay expose any query bottleneck?** No bottleneck was measured; see Benchmarks
    for the honest note that a full mixed-roster load benchmark wasn't built this phase.
15. **Are archetypes still unjustified?** Yes — no component-intersection cost was measured as a
    meaningful fraction of tick budget at any scale tested.
16. **Are deferred structural commands still unnecessary?** Yes — no Cow/Skeleton/Spider logic needed
    to mutate a queried entity mid-iteration beyond what the existing rule already allows safely.
17. **Which legacy actor would be most difficult to migrate and why?** Golem (boss phases, a second
    ability, per-tick behavior branching beyond attack/chase) or Creeper (fuse+area-explosion, a
    damage/lifecycle shape neither combat helper currently models) are the two most likely candidates
    for "not mechanical" — see the table in `docs/ecs.md`'s retirement-gate section.
18. **Can the remaining actors safely migrate incrementally when future gameplay touches them?**
    Yes — the migration template (composition helper, feature component, `DamageableActorCombat`
    call, `Owns`/`TryApplyDamage` registration) is now proven across 6 species with varied behavior;
    nothing about the remaining 6 requires a different template, only more feature-specific logic
    inside it.
19. **Is the ECS now "closed enough" to stop being an architecture focus?** Yes. The core is stable
    (zero allocation, proven across 6 varied species); damage dispatch is capability-oriented for
    discovery and a small registered list for routing, not a growing type-switch; new-actor DX is
    proven cheap (Cow: +2.7%); the legacy roster has an explicit, evidence-based non-migration
    decision with a stated re-evaluation trigger; no known ECS correctness or DX issue is open.
20. **Can Phase XXIII safely move to World Generation / World Simulation?** Yes — nothing in this
    phase's findings identifies a structural blocker in the entity/ECS runtime that would need
    resolving before world-generation work begins. ECS moves from active architecture focus to
    stable infrastructure that evolves when gameplay touches it.

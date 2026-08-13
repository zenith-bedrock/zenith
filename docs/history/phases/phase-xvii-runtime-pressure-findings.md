# Phase XVII — Gameplay Expansion & Runtime Pressure Findings

Phase XVI produced [docs/entities.md](../../entities.md), a catalog of what already exists — not an ECS
design. This phase adds three concrete, targeted gameplay features, one per priority named in the
brief, each chosen specifically to pressure a boundary the catalog identified rather than to add
content for its own sake:

- **Bat** (Priority 1) — the first entity whose movement does not fit the ground-mob assumption.
- **Spider** (Priority 2) — the second entity with proximity-triggered chase-and-attack targeting,
  deliberately shaped like Zombie's.
- **Villager** (Priority 3) — the first non-player entity carrying inventory-shaped data.

World lifecycle (chunk activation/spawning/despawning/population limits, Priority 4) was
investigated but not given a fourth new mob — see its own section below for why the existing three
additions already answer the question without needing a dedicated case.

All three shipped with tests (spawn, lifecycle, replication, late join, damage/death — per the
brief's testing checklist) and are wired into the composition root and diagnostics. Full suite:
747/747 passing (baseline 728 + 19 new), no regressions.

## Priority 1 — Bat: does `GroundMobMovement` remain correctly scoped?

**Yes, and the reason turned out more interesting than expected.** `Bat` implements `IGroundMob`
directly and reuses `GroundMobCombat` for all damage/death/loot/XP bookkeeping, unmodified. That
contract turned out to be movement-agnostic despite its name: `IGroundMob` requires identity,
position, health, damage, and removal — nothing about walking, standing, or any notion of "ground."
A flying mob satisfies it exactly as well as a walking one.

Movement is where the real difference is, and it stayed real: `BatSystem.TryFly` is a bespoke
private method, not a call into `GroundMobMovement.CanStandAt`. The two checks are genuinely
different rules — `CanStandAt` requires a solid block underneath (a real ground mob must be
supported); `TryFly` requires only body clearance, explicitly with no solid-block requirement,
and moves along a third (vertical) axis no ground mob's wander touches. Forcing these into one
function would mean adding a `bool requiresGroundSupport` parameter to a function whose entire
value was being a single, unconditional predicate — the kind of "just add a flag" pressure that
should be resisted, not accommodated, until a second flying mob makes the duplication real.

**Confirmed:** `GroundMobMovement.CanStandAt` stays exactly as scoped — ground-only, one predicate,
no parameters. **New observation:** `IGroundMob`/`GroundMobCombat`'s actual scope is broader than
their names suggest (movement-agnostic, not ground-specific). This is a naming-clarity note, not
an architecture problem — renaming was considered and rejected for this phase (see Rejected
approaches) because a single flying consumer isn't evidence the current name is actively
misleading anyone yet.

## Priority 2 — Spider: does targeting become a real shared capability?

**Not yet — and this phase generated the second data point on purpose, not a merge.**
`SpiderSystem`'s `FindOrAcquireTarget`/`IsTargetValid`/`AdvanceTowardTarget`/`TryAttackPlayer` are
near-verbatim copies of `ZombieSystem`'s, differing only in tuning constants (`DetectionDistance`
20 vs. 24, `MovePerTick` 0.11 vs. 0.08, `AttackDamage` 2 vs. 4, different loot/health/spawn point)
and the deliberate omission of Zombie's knockback. `Spider.TargetPlayerRuntimeId` has the exact
same field name and exact same acquisition/retention semantics as `Zombie.TargetPlayerRuntimeId`.

This is now genuinely at the project's own extraction threshold — "two is a coincidence, three is
a pattern" — with two real, identical-shape instances. It was **not extracted**, for the same
reason `GroundMobMovement` waited for a third walking-mob consumer before Phase XV: the rule is a
minimum bar, and two instances is exactly the coincidence case the rule exists to filter out. The
duplication is real and is now flagged as the leading Phase XVIII candidate (§ below) — the next
mob that needs proximity chase-and-attack (not fuse-trigger like Creeper, not damage-trigger like
Enderman) is the third instance that would justify extracting something like
`GroundMobTargeting.AcquireNearest`/`AdvanceToward`.

Contrast with what Creeper and Enderman already showed (Phase XV): Creeper's `TargetPlayerRuntimeId`
exists with the *same field name* as Zombie's but *different consumption* (proximity trigger for a
fuse, not a chase), and Enderman's `AggroTargetRuntimeId` is damage-triggered, not proximity — both
already-confirmed as legitimately different behaviors, not near-duplicates. Spider is the first mob
that duplicates Zombie's targeting *and* its consumption pattern, which is a materially different
and stronger signal than either of those.

**Confirmed:** the "two is a coincidence" rule holds under real pressure — it would have been easy
to extract at two instances given how identical the code reads side by side, and the project's own
prior precedent (`GroundMobMovement`) was followed instead. **New pressure point:** target
acquisition/chase/attack is now a documented, real, two-instance duplication — the most likely
single extraction to happen in Phase XVIII.

## Priority 3 — Villager: does inventory become a real shared capability outside Player?

**Only the data half was tested; the interaction half was deliberately deferred, and that's the
finding.** `Villager.Wares` is a `List<StackId>` — no slots, no hotbar, no equipment, nothing like
`PlayerInventory`'s shape. It is seeded once at spawn and never read by anything: no trade
interaction, no right-click handling, no transaction path exists. This mirrors the exact gap Phase
XVI documented for Cow's player-feed breeding trigger — `InventoryTransactionPacket
.TypeItemUseOnActor` only routes `ActorAttack` today, not a generic "interact with entity" action.
Building that plumbing was out of scope for this phase's actual question (runtime/entity-model
boundaries, not protocol surface), so it was deferred the same way, a second time.

What this *does* answer: `PlayerInventory` and `Villager.Wares` do not resemble each other at all
once real data exists for both — one is slot-indexed with equipment/hotbar concerns, the other is
a flat bag with none. There is no pressure to unify them, because there is nothing to unify; they
solve different problems that happen to both be called "inventory" in gameplay language. What this
does *not* answer: whether a real trade/interaction feature would create pressure on `Villager`,
`PlayerInventory`, or the protocol boundary, because no interaction was implemented. This remains
an open question, not a rejected one — see Phase XVIII candidates below.

**Confirmed:** `PlayerInventory` stays player-specific; no shared inventory primitive is
justified. **New pressure point (unresolved):** the interaction/transaction gap is now twice-
documented (Cow breeding, Villager trade) and is the more likely trigger for real architectural
pressure than the data shape itself.

## Priority 4 — World lifecycle: does a true shared ownership boundary appear?

No fourth mob was built for this priority — the three additions above already generate the
relevant evidence without needing a dedicated case:

- **Chunk activation / spawning**: Bat, Spider, and Villager all use the exact same
  bootstrap-spawn-near-first-player pattern as every prior mob, and the exact same
  `ActorInterest.Includes` viewer-reconciliation loop (six now-independent copies of this loop
  exist across the six ground-mob-shaped systems — a real, still-unextracted duplication first
  flagged in Phase XVI's catalog).
- **Despawning**: all three reuse `GroundMobLifecycle.EvaluateDespawn` completely unmodified,
  including Bat despite being a flying mob — this is further confirmation (beyond Phase XVI's
  five ground mobs) that the despawn rule is genuinely position/visibility-based, not
  movement-mode-based.
- **Population limits**: none of the three new mobs added a cap. Only `CowSystem.MaxPopulation`
  exists, because only Cow breeds. Population limits are a consequence of *reproduction* existing,
  not of being a mob — Spider and Villager don't reproduce, so they don't need one, and Bat
  doesn't either. This is evidence the population-cap concept is correctly scoped to
  "self-replicating populations," not "all mobs."

**Confirmed:** `GroundMobLifecycle`, `ActorInterest`, and chunk-knowledge-based visibility
(Floor Drop's separate mechanism, untouched by this phase) remain three legitimately different,
correctly-scoped mechanisms — nothing this phase added blurred the boundary Phase XVI already
drew. **No true shared ownership boundary appeared** beyond what was already confirmed; population
limits specifically are tied to breeding, not to "being a world object," and generalizing them now
would be solving a problem that doesn't exist yet (no second breeding mob).

## New confirmed abstractions

None. This phase deliberately extracted nothing — every new mob either reused an existing shared
primitive unmodified (`GroundMobCombat`, `GroundMobMovement` reused by Spider/Villager,
`GroundMobLifecycle` reused by all three) or introduced bespoke, single-consumer logic that stayed
local (`BatSystem.TryFly`, Spider's duplicated targeting, `Villager.Wares`). The fact that nothing
new needed extracting is itself a confirming result for the existing primitives' boundaries — they
absorbed three structurally different new mobs (flying, second-hostile, first-NPC-with-data)
without needing to change shape.

## Still concrete concepts (look similar, stay separate)

- **`Zombie.TargetPlayerRuntimeId` vs. `Spider.TargetPlayerRuntimeId`** — identical name and
  semantics, but still two separate fields on two separate types, not a shared interface member.
  Two instances, not merged (see Priority 2).
- **`Villager.Wares` vs. `PlayerInventory`** — both informally "inventory," structurally
  unrelated (flat list vs. slot-indexed container with equipment/hotbar). Confirmed different, not
  merged.
- **`Bat`'s flight validity vs. `GroundMobMovement.CanStandAt`** — both "can this mob be at this
  position," but opposite answers to "does this position need a supporting block." Confirmed
  different, not merged.
- **Cow's wander (`Cow.WanderDirectionX/Z`) vs. Villager's wander (`Villager.WanderDirectionX/Z`)**
  — now a second instance of proximity-free 2D random-heading wander, structurally identical to
  Cow's `Wander`/`PickNewHeading`. Like Spider's targeting, this is real duplication at exactly two
  instances and was deliberately left unextracted for the same reason.

## Rejected approaches

- **Renaming `IGroundMob`/`GroundMobCombat` now that Bat proved they're movement-agnostic.**
  Rejected as premature: one flying consumer clarifies the interface's actual scope but doesn't yet
  make the "ground" name actively misleading in practice — nobody has been confused by it, and a
  rename mid-phase for a single new consumer would be exactly the kind of speculative churn this
  project's methodology avoids. Revisit only if a second flying/swimming mob makes "ground" read as
  actively wrong to a new contributor, not preemptively.
- **Extracting `GroundMobTargeting` from Zombie + Spider.** Two real, identical-shape instances is
  the coincidence case, not the pattern case, by this project's own established threshold. Deferred
  to the third instance, per precedent (`GroundMobMovement` in Phase XV).
- **Extracting a shared `Wander` helper from Cow + Villager.** Same reasoning — two instances,
  deferred to a third.
- **Building the interact/trade protocol path for Villager.** Out of scope for this phase's
  question (runtime/entity-model boundaries), and the second occurrence of a gap Phase XVI already
  chose to defer rather than solve piecemeal — better addressed as its own scoped phase once (or
  if) real trade gameplay is prioritized.
- **A world-lifecycle "ownership boundary" abstraction (Priority 4).** No evidence appeared that
  spawning/despawning/population-limits need a shared owner beyond what already exists
  (`GroundMobLifecycle` for despawn, per-system bootstrap for spawn, `CowSystem.MaxPopulation` as a
  concrete local cap). Introducing one now would be designing for a category of pressure
  (world-scoped systems needing to coordinate across mob types) that hasn't appeared.

## Phase XVIII candidates (evidence-gated only)

- **`GroundMobTargeting`** (or similarly named) extraction, triggered by a *third* mob needing
  proximity-triggered chase-and-attack (not fuse-trigger, not damage-trigger). This is now the
  single most concretely-evidenced candidate in the project — two real, identical instances exist
  today (Zombie, Spider).
- **A shared `Wander` helper**, triggered by a third mob needing proximity-free 2D random-heading
  wander (Cow, Villager are the first two).
- **Villager trade interaction**, as its own scoped phase: implementing
  `InventoryTransactionPacket`'s interact-with-entity path would test whether `Villager.Wares`
  needs to grow into something more inventory-shaped, and whether a shared
  interaction/transaction concept is real — neither of which this phase could answer without
  building the protocol work first.
- **A second flying or swimming mob**, which would be the actual trigger to reconsider whether
  `IGroundMob`/`GroundMobCombat`/`GroundMobLifecycle` should be renamed to reflect their real
  (movement-agnostic) scope, or whether flight/swim movement deserves its own shared
  `TryFly`/`TrySwim`-shaped primitive the way `GroundMobMovement` did for walking.
- **The six-copy viewer-reconciliation loop** (`ActorInterest`-based `ReconcileViewers`, now
  present in Bat/Spider/Villager too — nine copies total across all ground-mob-shaped systems
  including Zombie/Skeleton/Cow/Creeper/Enderman). Flagged in Phase XVI as a maintenance-pressure
  candidate; still not extracted, but the count keeps growing and is worth a dedicated audit before
  it reaches ten.

## What new runtime concepts appeared because gameplay demanded them?

None. That is the finding. Three structurally different new mobs — one with a genuinely new
movement mode, one that intentionally duplicates an existing behavior shape, one carrying a new
kind of data — were all absorbed by the existing primitives (`IGroundMob`, `GroundMobCombat`,
`GroundMobMovement`, `GroundMobLifecycle`) without any of them needing to change, and without any
new shared concept needing to be invented. The two real duplications this phase surfaced
(target-acquisition-and-chase, proximity-free wander) are exactly at the "two instances, not yet a
pattern" threshold the project has used consistently since Phase XIV.1 — real, documented, and
deliberately left alone. The architecture was tested by design this phase, not merely extended,
and it held.

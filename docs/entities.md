# Entity Runtime Catalog & Future ECS Pressure Map

Living document. Purpose: give architectural visibility into Zenith's runtime categories before
any future expansion, not to design or implement an ECS/component system. §1–§10 are a snapshot of
the pre-ECS runtime as of Phase XX; §11–§12 record the real ECS adopted in Phase XXI/XXII. It
should be revisited as gameplay grows, and specific claims should be re-verified against the code
rather than trusted indefinitely.

**Phase XX renamed `IGroundMob` to `IDamageableActor`** (see §10) — sections below dated Phase XIX
and earlier use `IGroundMob` because that was its name at the time and this document preserves the
historical narrative; every such reference means the same type now called `IDamageableActor`.
`GroundMobCombat`/`GroundMobMovement`/`GroundMobLifecycle` keep their names — see §10 for why.

Companion reading: [phase-xiv.1](history/phases/phase-xiv-gameplay-expansion-findings.md) through
[phase-xx](history/phases/phase-xx-runtime-relationships-findings.md) findings docs cover the incremental evidence
that produced `IDamageableActor`(née `IGroundMob`)/`GroundMobCombat`/`GroundMobMovement`/
`GroundMobLifecycle` and then pressure-tested them with Bat (flight), Spider (second
chase-targeting mob, later given the project's first mob-sourced status effect), Villager (first
non-player inventory-shaped data, later given a real trade interaction), Golem (boss-shaped
complexity, and a real entity-interact wire consumer alongside Cow's feed trigger), Minecart (first
entity category deliberately built outside "mob," later given a real player↔vehicle riding
relationship), and Fish (second non-ground-navigation mob — swimming, a third distinct
movement-validity model). This document widens the lens to every runtime category, not just
ground mobs.

## Principle this document follows

Zenith models runtime state as **Identity + Data + Behavior/System ownership**, not as a class
hierarchy. There is no `Entity` base type, no `LivingEntity`, no `MobBase`. Each category
(`Player`, ground mob species, `Projectile`, floor drop, falling block) is a concrete type with
its own fields; shared bookkeeping is extracted only after real, repeated duplication across
concrete consumers (Phase XIV.1's rule: two is a coincidence, three is a pattern). This document
does not propose changing that. It catalogs where categories already share shape, and — for
categories that only *look* similar — records why they were kept separate.

---

## 1. Runtime Entity Categories

| Category | Examples | Lifecycle | Persistence | Replication | Current owner |
|---|---|---|---|---|---|
| Player | Human-controlled player | Connection-driven (`IsInGame`/`IsSpawning`); never despawns | Yes — `pd:{uuid}`, `inv:{uuid}`, `ar:{uuid}` LevelDB keys (`WorldStorageKeys`, `PlayerDataBlob`, `World.PersistInventory`/`PersistPlayerData`) | Event-driven fan-out on join/leave/state-change (`PlayerVisibility.AnnounceJoin/AnnounceLeave/RelayHealth/RefreshPeerView`) | `PlayerManager`, `Session/*Handler` |
| Ground Mob | Zombie, Skeleton, Cow, Creeper, Enderman, Spider, Villager, Golem | `GroundMobLifecycle.EvaluateDespawn` — resets on player proximity, expires after `DefaultDespawnTicks = 6000` unseen | No — RAM only, per-species `List<T>` store (`ZombieStore`, `CowStore`, etc.) | Per-tick `ActorInterest.Includes(peer, x, z)` viewer reconciliation | Per-species `*System.cs` (`ZombieSystem`, `CowSystem`, ...) |
| Flying Mob | Bat | Same `GroundMobLifecycle.EvaluateDespawn` as ground mobs — despawn turned out to be position/visibility-based, not movement-mode-based | No — RAM only, `BatStore` (same `List<T>` shape) | Same `ActorInterest.Includes` mechanism as ground mobs | `BatSystem` |
| Swimming Mob | Fish | Same `GroundMobLifecycle.EvaluateDespawn` as every other category — third confirmation this is movement-mode-agnostic | No — RAM only, `FishStore` (same `List<T>` shape) | Same `ActorInterest.Includes` mechanism | `FishSystem` |
| Vehicle | Minecart | Same `GroundMobLifecycle.EvaluateDespawn` as every other `IDamageableActor` | No — RAM only, `MinecartStore` (same `List<T>` shape) | Same `ActorInterest.Includes` mechanism | `MinecartSystem` |
| Projectile | Arrow (via `ProjectileSystem`) | Pure age: `AgeTicks >= MaximumLifetimeTicks (80)`, independent of visibility; also removed on world/entity hit | No — RAM only, `ProjectileStore` (`SoftCap 2048`) | Per-tick `ActorInterest.Includes` viewer reconciliation (same mechanism as ground mobs) | `ProjectileSystem` |
| Item / Floor Drop | Dropped items on the ground | Pure age: `AgeTicks >= FloorDropStore.DefaultDespawnTicks (6000)`, independent of visibility | No — RAM only, cell-keyed dictionary in `FloorDropStore` | `peer.Chunks.Knows(cx, cz)` (chunk-knowledge gate, not `ActorInterest`) via `FloorDropFanout.Publish` | `FloorDropSystem`, `FloorDropFanout` |
| Falling Block | Sand/gravel mid-fall | Landing-triggered removal — no age timer, no despawn window | No — `FallingBlockEntry` explicitly documented "RAM-only — never persisted" | Broadcast to all `peer.IsInGame` — no interest gating at all | `GravitySystem`, `FallingBlockStore` |

Four distinct despawn/removal shapes exist across categories: connection-driven (Player),
visibility-reset age (ground mob and — as of Phase XVII — flying mob too), pure age (Projectile
and Floor Drop — same shape, different owners), and event-triggered with no timer (Falling Block).
Three distinct replication-gating mechanisms exist: `ActorInterest` (ground mob, flying mob,
Projectile), chunk-knowledge (Floor Drop), and unconditional broadcast (Falling Block). These are
recorded as **confirmed differences**, not gaps to close.

**Phase XVII note:** "Flying Mob" is listed as its own row for readability, but `Bat` is not a
separate runtime type from `IDamageableActor`'s perspective — it implements the same interface as
every ground mob and reuses `GroundMobCombat`/`GroundMobLifecycle` unmodified. Only its movement
(`BatSystem.TryFly`, bespoke, not `GroundMobMovement.TryMoveHorizontal`) differs. See
[phase-xvii findings, Priority 1](history/phases/phase-xvii-runtime-pressure-findings.md) for why this was kept
as one interface rather than split.

**Phase XX note:** "Swimming Mob" is the same situation — `Fish` is a third confirmation (after
Bat, Minecart) that the interface (renamed this phase to `IDamageableActor`, see §10) describes a
runtime shape broader than "ground" or "mob." `FishSystem.TrySwim` is its own third distinct
movement-validity predicate, alongside `GroundMobMovement.TryMoveHorizontal` (ground) and `BatSystem
.TryFly` (flight) — see [phase-xx findings, Priority 2](history/phases/phase-xx-runtime-relationships-findings.md)
for the three-way comparison.

**Phase XIX note:** "Vehicle" is the same situation as "Flying Mob" above — `Minecart` is not a
separate runtime type either, just a third confirmation that `IGroundMob` and its four helpers
describe "positioned, health-bearing, replicated, despawn-eligible thing," not "mob." What makes
Minecart a vehicle (no AI, moved only by external push) is gameplay information layered on top,
the same way "Mob" itself is gameplay information layered on top of the same interface. See
[phase-xix findings, Priority 1](history/phases/phase-xix-runtime-pressure-findings.md).

---

## 2. Gameplay Capability Matrix

YES = concrete field/method exists. PARTIAL = the concept exists but not as a first-class field
(e.g. computed rather than stored). NO = absent.

| Capability | Player | Mob | Projectile | ItemEntity | WorldObject |
|---|---|---|---|---|---|
| Position | YES `PositionX/Y/Z` | YES `PositionX/Y/Z` | YES `PositionX/Y/Z` | PARTIAL — cell key `(X,Y,Z)`, no persistent float fields | YES `LandX/LandZ` (int, immutable) + `Y` (float) |
| Rotation | YES `Pitch/Yaw/HeadYaw` | PARTIAL — `Yaw` only, no pitch | NO | NO | NO |
| Velocity | PARTIAL — input-driven, no stored velocity | PARTIAL — Zombie's transient `KnockbackVelocityX/Z` and Minecart's `VelocityX/Z` (Phase XIX, same impulse-plus-decay shape, different trigger); Bat has a live per-tick heading (`WanderDirectionX/Y/Z`) instead | YES `VelocityX/Y/Z` | NO — static cell | YES `VelocityY` |
| Health | YES `HealthState` | YES `HealthState` per species (11 species as of Phase XX, health ranging 3–100) | NO | NO | NO |
| Damage (deals or takes) | Takes — `ApplyDamage` via `PlayerDamage.Apply` | Takes — `IDamageableActor.ApplyDamage`, shared via `GroundMobCombat.TryApplyDamage<TMob>`; Golem also *deals* one-to-many via a plain loop over `PlayerDamage.Apply`; Spider also *deals* a status effect alongside damage | Deals only — no `ApplyDamage` | NO | NO |
| Inventory | YES `PlayerInventory` (slot-indexed, hotbar, equipment) | PARTIAL — `Villager.Wares` (`List<StackId>`, flat, no slots), backing a real trade; every other species NO | NO | N/A — the drop *is* an item, not a container | NO |
| Ownership | NO | NO | YES `OwnerRuntimeId` (immutable) | NO | NO |
| Relationship (controls/rides) | PARTIAL (Phase XX) — `Player.RidingEntityId`, one narrow `MovementSystem` carve-out | PARTIAL (Phase XX) — `Minecart.OccupantPlayerRuntimeId`, the only mob-shaped category with this; every other species has none | NO | NO | NO |
| Targeting | NO | PARTIAL — Zombie/Spider `TargetPlayerRuntimeId` (retained, same proximity-chase semantics — still 2 identical instances after Phase XX), Creeper `TargetPlayerRuntimeId` (different fuse-trigger semantics), Enderman `AggroTargetRuntimeId` (damage-triggered), Golem — fresh per-tick scan, a fourth distinct shape; Skeleton/Cow/Bat/Villager/Minecart/Fish have none | NO | NO | NO |
| Effects (status) | YES `Player.Effects` (player-submitted via `EffectIntent`) | PARTIAL — Spider *applies* Poison to a player it hits (`SpiderSystem.TryApplyPoison` writes directly to the same `Player.Effects`); no mob has ever *received* a status effect itself | NO | NO | NO |
| Lifetime | NO (never expires) | YES `LastSeenNearPlayerTick` + `GroundMobLifecycle` — now 11 consumers, still position/visibility-based regardless of movement mode, behavior complexity, or relationship state | YES `AgeTicks` / `MaximumLifetimeTicks` | YES `AgeTicks` / `DefaultDespawnTicks` | NO |
| Persistence | YES | NO | NO | NO | NO |
| Interest visibility | N/A | YES `ActorInterest.Includes` (all 11 species, via the shared `ViewerReconciliation.Sync` extracted in Phase XX) | YES `ActorInterest.Includes`, same shared `ViewerReconciliation.Sync` | PARTIAL — chunk-knowledge, not `ActorInterest` | NO — unconditional broadcast |
| Interaction (beyond attack) | N/A | YES (real) — Villager trade, Cow feed, and Minecart mount/dismount (Phase XX, superseding Phase XIX's push) all consume `InventoryTransactionPacket.ActorInteract` via `Player.TryConsumeInteractIntent`; every other species still attack-only | NO | YES — pickup via AABB reach check | NO |
| AI | N/A | YES — per-species (chase, wander, fuse, teleport, 3D wander, phase-based boss); ground movement via `GroundMobMovement.TryMoveHorizontal`, flight via Bat's bespoke `TryFly`, swimming via Fish's bespoke `TrySwim` (Phase XX, a third distinct validity rule); Minecart has none at all — moves only when ridden | NO | NO | NO |
| Physics | Client-authoritative movement, not reviewed here | Phase XXIX — real gravity/falling/landing/step-up for ground-walking species (`GroundMobMovement.ResolveVertical`, same 0.08/0.98/3.92 constants as `GravityPerTick` below); Minecart/Enderman keep step-validity-only (`IsSupportedGroundCell`, no vertical model); `TryFly`/`TrySwim` unaffected | YES — gravity (`GravityPerTick`), block-hit detection | NO | YES — gravity + drag (`Gravity`, `DragFactor`) + landing/collision |

The goal of this matrix is not to identify components to build. It is to make visible which
capabilities are genuinely repeated (Position across all categories; Lifetime as *two different
shapes* shared across categories; Interest-style visibility as *three different mechanisms*) versus
which only sound similar (Targeting: 2 identical instances (Zombie/Spider) + 2 differently-shaped
single instances (Creeper, Enderman) + a 4th distinct shape (Golem's unretained scan) + mobs with
none; AI is entirely per-species with no shared representation beyond the movement-validity
probes; Inventory: Player's and Villager's are both called "inventory," now both exercised by real
mutation paths (Phase XVIII), and still share no structure).

---

## 3. Future ECS Pressure Candidates

Analysis only — none of the below are implemented, and none should be until the evidence bar in
§5 is met.

### PositionComponent

Used by: Player, Mob (all 5 species), Projectile, Falling Block (partially — Floor Drop is
cell-keyed, not float-field-based).

Evidence: position is duplicated as three parallel float fields in every category above except
Floor Drop. No category currently needs to iterate positions *across* categories in one pass —
each system (`ZombieSystem`, `ProjectileSystem`, ...) only ever iterates its own store.

Question: does a future system need to process all positions together (e.g. a single spatial
index for all replicable objects, instead of one `ActorInterest` scan per system per tick)? No
such system exists yet — each `Tick()` re-derives its own interest set independently, which is
duplication of *mechanism*, not yet duplication of *data*.

### LifetimeComponent (age-based despawn)

Used by: ground mob (`GroundMobLifecycle`, visibility-reset), Projectile (pure age), Floor Drop
(pure age).

Evidence: three real implementations, two of which (Projectile, Floor Drop) are structurally
identical (age counter, single threshold, no visibility interaction) and one of which (ground
mob) is deliberately different (visibility resets the counter). This was investigated directly in
Phase XVI and rejected as a merge target — see §4.

Question: if a fourth category needs *pure*-age expiry (not visibility-reset), is that the
trigger to extract `AgeExpiry.HasElapsed(ageTicks, threshold)` as a one-line shared predicate?
Possibly — but note the two existing pure-age implementations (`ProjectileSystem`,
`FloorDropStore`) have not been merged despite already being identical in shape, because the
"extraction" would save less than five lines per site. This is a maintenance-pressure question,
not a data-pressure one.

### InterestVisibilityComponent

Used by: ground mob, Projectile (`ActorInterest.Includes`); Floor Drop (chunk-knowledge,
different mechanism); Falling Block (none).

Evidence: two categories already share the exact same call (`ActorInterest.Includes(peer, x,
z)`), invoked independently inside 6 different systems' `Tick()` methods with near-identical
reconcile-viewers loops (`_replicated` hash set, add-on-enter, remove-on-exit-or-despawn).

Question: is per-system viewer reconciliation (the loop shape, not just the `ActorInterest` call)
becoming a real repeated concept? This is the strongest current candidate for a shared helper —
six independent copies of the same ~15-line loop — but it was not in scope for Phase XVI and has
not been measured for actual maintenance cost (bugs from drift between copies, time spent editing
all six for one behavior change). Worth flagging for a dedicated audit before Phase XVII, not
extracting speculatively here.

### TargetingComponent

Used by: Zombie, Creeper (`TargetPlayerRuntimeId`), Enderman (`AggroTargetRuntimeId`).

Evidence: three fields with different names and different acquisition triggers (Zombie:
proximity scan; Creeper: proximity scan, shared shape with Zombie but separate field; Enderman:
damage-triggered, not proximity). Skeleton and Cow have no targeting concept at all.

Question: is this "the same capability with naming drift" or "three independent single-target
trackers that happen to all be `long?`"? Given the acquisition logic differs per mob and no
system currently reads another mob's target field, this reads as the latter — see §4 Rejected.

---

## 4. Current Architecture Validation

### Confirmed

```
GroundMobCombat
GroundMobMovement
GroundMobLifecycle
```

Why: each has ≥3 real concrete consumers with identical semantics (damage/death/loot/XP
bookkeeping; position-validity probing; visibility-reset despawn), extracted only after the
duplication was observed across real implementations, not anticipated. All three stayed small
under Phase XV/XVI pressure — new mob-specific behavior (Zombie knockback, Cow breeding) landed
entirely outside them rather than forcing them to grow.

### Rejected

```
BaseEntity / LivingEntity / MobBase
GenericEntityStore<T>
PositionComponent / LifetimeComponent / TargetingComponent (as implemented components)
Full ECS
```

Why:

- **Lifecycle differs per category.** Four different despawn/removal shapes exist (§1). A shared
  base or component would have to either abstract over genuinely incompatible removal semantics,
  or degrade into a marker with no real behavior.
- **Persistence differs per category.** Only Player persists to disk. A `BaseEntity` spanning
  Player and everything else would carry persistence concerns that 4 of 5 categories don't need.
- **Replication differs per category.** Three different gating mechanisms (`ActorInterest`,
  chunk-knowledge, unconditional broadcast) are in active use — not an oversight, but a fit to
  each category's actual visibility needs (a falling block is short-lived and cheap enough to
  broadcast; a floor drop needs chunk-knowledge because it can outlive a player's session-relevant
  interest window; ground mobs and projectiles need the finer-grained per-tick check).
- **`GenericEntityStore<T>`**: the five ground-mob stores plus `ProjectileStore` are six
  structurally identical `List<T>` wrappers (documented in
  [phase-xvi findings](history/phases/phase-xvi-entity-runtime-findings.md)), which is real duplication — but
  not yet extracted, because no measured maintenance cost has appeared. This is the
  closest-to-justified rejected candidate on this list; see §6 for its trigger condition.
- **Full ECS**: no category currently needs an arbitrary combination of capabilities assembled at
  runtime. Every category's capability set (§2) is fixed at compile time by its concrete type.
  Composition pressure (§5) has not appeared.

---

## 5. ECS / Hybrid Runtime Trigger Conditions

Not reconsidered because "there are many entities," "Minecraft uses ECS," or "performance" in the
abstract. Reconsidered only when one of the following is *measured*, not anticipated:

### Data-oriented pressure

- Thousands of objects of one category are processed every tick and profiling shows the
  per-object dispatch or cache-miss cost is a measured fraction of the tick budget (Phase
  XIII/XIV benchmarking established the pattern: use `zenith.Benchmarks --runtime-load`, don't
  guess). Current ground-mob counts (bootstrap-spawned, single digits per species in tests, no
  configured population beyond `CowSystem.MaxPopulation = 32`) are nowhere near this.
- The same system needs to iterate unrelated categories in one pass (e.g. a single spatial query
  touching Player + Mob + Projectile positions together) and the cost of running N separate scans
  is measured, not assumed.

### Composition pressure

- A new entity needs an arbitrary combination of capabilities from §2 that doesn't cleanly fit
  the "ground mob," "projectile," "item," or "world object" shape — e.g. something that has
  Health + Inventory + Movement + Targeting + Lifetime simultaneously, where forcing it into an
  existing category class produces an obviously wrong shape (a mob with an inventory; an item
  that has AI). None of the current gameplay pressure roadmap (§6) items are guaranteed to hit
  this — it should be evaluated per feature as it lands, not pre-built for.

### Maintenance pressure

- A single gameplay change requires editing ≥3 unrelated categories' systems in lockstep (e.g. a
  new "on-hit visual effect" that must be added identically to `ZombieSystem`,
  `ProjectileSystem`, and `FloorDropSystem`).
- The six-copy viewer-reconciliation loop flagged in §3 causes a real bug from drift between
  copies, or a feature change that must touch all six.
- A sixth `List<T>`-shaped store appears needing the same soft-cap behavior `ProjectileStore`
  already has (this specific trigger was already identified in the
  [Phase XVI findings](history/phases/phase-xvi-entity-runtime-findings.md) as the leading Phase XVII candidate).

---

## 6. Future Gameplay Pressure Roadmap

Each area below is a candidate source of the evidence §5 requires — not a queue of features to
build for their own sake. For each: what it would pressure, and what evidence would justify
evolving the architecture.

### Player

- **PvP** — pressures `PlayerDamage`/`HealthState` (already shared with mob damage via
  `DamageSource`). Evidence to watch for: does PvP need a damage-type or combat-state concept
  that doesn't fit the existing `DamageCause` enum?
- **Trading** — pressures Inventory + Interaction. Evidence: does a trade need a transaction
  concept beyond `InventoryStackAction` (flagged as a possible future pressure point in
  [phase-xiii-inventory-action-review.md](history/phases/phase-xiii-inventory-action-review.md))?
- **Quests / progression** — pressures persistence (`PlayerDataBlob`) and possibly a new
  "objective" runtime concept with no current analog anywhere in the catalog. Evidence: does a
  quest need to track state for non-player entities too (e.g. "kill 5 zombies"), which would be
  the first cross-category state-tracking need?
- **Advanced effects** — pressures the existing `EffectSystem`/effects dict on Player. Evidence:
  do mobs need the same effect representation (currently mob-only state like `IsFusing` or
  `AggroTicksRemaining` is bespoke per species, not effect-system-driven)?

### World

- **Weather / seasons / events** — no current analog; these are world-scoped, not entity-scoped.
  Evidence: do they need to apply per-entity modifiers (e.g. rain slows fire spread, snow affects
  mob spawn rates) that would require a new "environment affects entity" hook shared across
  categories?
- **Chunk lifecycle** — already exists (`ChunkStreamSystem`); pressures interest/visibility
  mechanisms directly. Evidence: does chunk unload need to interact with all 5 replication
  mechanisms in §1 identically, or does each category's current bespoke handling remain correct?

### Entities

- **More mobs** (flying/swimming navigation, hordes, trading NPCs, pets, bosses, vehicles) — the
  most direct pressure on this catalog. Evidence to watch for, mapped to §3's candidates:
  - A flying or swimming mob would be the first real test of whether `GroundMobMovement` should
    stay ground-specific or whether a `NavigationMode` concept is real (§3 Navigation Types
    question).
  - A second mob needing group/horde targeting would be the first real test of whether
    `TargetingComponent` (§3) is a coincidence or a pattern — right now it's 1 of 5.
  - An NPC/pet/villager needing an inventory would be the first entity outside Player to need
    one, testing whether Inventory is Player-specific or a broader capability.
- **Bosses** — likely to need multiple simultaneous behaviors (phases, multiple attacks) — a
  reasonable place to notice if a single mob's behavior outgrows a flat `*System.cs` file, which
  would be composition pressure (§5) at the single-entity level rather than across many entities.

### Inventory

- **Crafting / enchantments / item behaviors** — pressures `PlayerInventory` and
  `InventoryStackAction`. Evidence: already tracked in
  [phase-xiii-inventory-action-review.md](history/phases/phase-xiii-inventory-action-review.md); re-check that
  review's conclusions once crafting is real, not before.

### Economy

- **Trading / villagers / merchants** — pressures Interaction (currently only Floor Drop has
  real player-interaction beyond attack) and Inventory (would be the first non-player inventory).
  Evidence: does a villager's trade inventory need the same `PlayerInventory` shape, or a
  simpler one? This is the most likely near-term source of "does Inventory generalize beyond
  Player" evidence.

---

## Definition of Done — answered

1. **What is an entity in Zenith today?** There is no single runtime type called "Entity." There
   are five independently-typed runtime categories (Player, ground mob species, Projectile, Floor
   Drop, Falling Block), each with its own identity, data, and owning system, unified only by
   sharing *some* subset of the capabilities in §2 — never all of them, never through a common
   base type.
2. **What is only a gameplay category?** "Mob" — it groups Zombie/Skeleton/Cow/Creeper/Enderman
   by gameplay role (a living, AI-driven creature), not by runtime representation. The runtime
   contract they actually share is `IGroundMob` (identity + position + health + damage +
   lifecycle), which is narrower than "everything a mob conceptually is" (no shared AI type, no
   shared targeting type).
3. **Which concepts are truly shared?** Position (universal, though Floor Drop stores it as a
   cell key); damage/health bookkeeping across Player and ground mobs (via `DamageSource` +
   `HealthState`, even though the funnel methods — `PlayerDamage.Apply` vs.
   `GroundMobCombat.TryApplyDamage` — are separate); pure age-based expiry between Projectile and
   Floor Drop; the `ActorInterest` visibility check between ground mobs and Projectile; and the
   viewer-reconciliation *loop shape* (not yet extracted) across all six per-tick systems.
4. **Which concepts only look similar?** Targeting (3 different fields, 3 different acquisition
   triggers, no shared type); AI (entirely per-species, only the movement-validity probe is
   shared); Lifetime (two structurally different mechanisms — visibility-reset vs. pure age —
   sharing a constant value by coincidence of tuning, not by shared implementation); Interest
   visibility (three different mechanisms, not one).
5. **Where would ECS/hybrid architecture actually provide value?** Nowhere yet, by the evidence
   in §5. The closest candidates are the six-copy viewer-reconciliation loop and the six
   structurally-identical `List<T>` stores (§3, §4) — both maintenance-pressure candidates, not
   data-oriented or composition-pressure ones, and neither has crossed the extraction threshold
   used throughout this project (three-plus consumers *and* a measured cost, not just a count).
6. **What gameplay should be implemented next to generate better evidence?** Per §6, the highest-
   signal candidates are: a non-ground-navigation mob (tests whether `GroundMobMovement` is
   ground-specific or should generalize), a second mob needing shared targeting/group behavior
   (tests whether `TargetingComponent` is real), and villager/trader inventory (tests whether
   Inventory generalizes beyond Player). Each would either confirm or reject a specific §3
   candidate with real evidence, rather than adding gameplay that just adds more of what's
   already proven (a sixth hostile ground mob would mostly re-confirm conclusions already drawn
   in Phase XIV–XVI).

   **Phase XVII resolved all three of these directly** — see §7 below for the outcome of each.

---

## 7. Phase XVII Checkpoint

Phase XVII implemented exactly the three gameplay cases §6 above called for: Bat (non-ground
navigation), Spider (second proximity-chase-targeting mob), Villager (first non-player
inventory-shaped data). Full detail: [phase-xvii findings](history/phases/phase-xvii-runtime-pressure-findings.md).
Net result: none of the three §3 candidates they were meant to test crossed the extraction
threshold — two moved from 1 instance to 2 (targeting, wander), one was answered in the negative
(inventory does not generalize), and one confirmed a boundary already suspected but untested
(flight movement is genuinely different from ground movement, and despawn is genuinely the same
regardless of movement mode).

### Data pressure

**None.** No hot loop processes Player + Mob + Projectile (or any other cross-category
combination) together. Every system — including the three new ones, `BatSystem`, `SpiderSystem`,
`VillagerSystem` — still iterates only its own store (`_bats.Active`, `_spiders.Active`,
`_villagers.Active`). Mob counts remain small (bootstrap-spawned, single digits per species in
practice; `CowSystem.MaxPopulation = 32` is still the only configured ceiling anywhere). Nothing
in this phase measured or even suggested a cache-locality or iteration-cost problem.

### Composition pressure

**None new.** Every new entity fit an existing category shape without needing an arbitrary
capability combination: Bat is exactly `IGroundMob` + bespoke movement (no new capability
combination — Health+Position+Damage+Lifetime is the same set every ground mob already has).
Spider is exactly the Zombie shape. Villager is exactly the Cow shape plus one new field
(`Wares`) that nothing yet reads. No entity needed Health + Inventory + Movement + Targeting +
Lifetime simultaneously in a way that didn't fit `IGroundMob`'s existing five members plus
per-species extra fields.

### Maintenance pressure

**Real, but still below the extraction bar, and now with two concrete instances instead of
speculation:**

- Target-acquisition-and-chase (`FindOrAcquireTarget`/`IsTargetValid`/`AdvanceTowardTarget`/
  `TryAttackPlayer`) is now duplicated byte-for-byte in shape between `ZombieSystem` and
  `SpiderSystem`. A change to chase behavior today requires editing both.
- Proximity-free 2D wander (`Wander`/`PickNewHeading`) is now duplicated byte-for-byte in shape
  between `CowSystem` and `VillagerSystem`.
- The `ActorInterest`-gated viewer-reconciliation loop (`ReconcileViewers`) is now duplicated nine
  times (Zombie, Skeleton, Cow, Creeper, Enderman, Projectile, Bat, Spider, Villager) — up from six
  at the end of Phase XVI.

None of these were extracted this phase, consistent with the "three is a pattern" rule — the first
two are each at exactly two instances, and the viewer-reconciliation loop, despite nine copies, has
still not caused a measured bug or a change that had to touch more than one or two of them at a
time in this phase. All three are now the leading, concretely-evidenced Phase XVIII candidates
(see [phase-xvii findings](history/phases/phase-xvii-runtime-pressure-findings.md) for the fuller reasoning).

---

## 8. Phase XVIII Checkpoint

Phase XVIII resolved the interaction gap this document (and every findings doc since Phase XVI)
had flagged and deferred, and added one boss-shaped entity (`Golem`) specifically to generate a
real third data point for the targeting-extraction question §7 left open. Full detail:
[phase-xviii findings](history/phases/phase-xviii-runtime-pressure-findings.md). Net result: the interaction gap
turned out to be a protocol-dispatch omission, not a missing runtime abstraction — the wire layer
already decoded everything needed (`TargetActorRuntimeId`, `ActorInteract` vs. `ActorAttack`); it
was simply never routed anywhere. And the targeting question got a real "no": Golem was built with
genuinely different (unretained, re-scanned-every-tick) targeting rather than being forced into
Zombie/Spider's shape, so that duplication honestly remains at two instances, not three.

### Interaction (new this phase)

`Player.SubmitInteractIntent`/`TryConsumeInteractIntent(long expectedTargetRuntimeId)` — a new
`PendingValue<long>` mailbox — plus a small, genuinely generic addition to the shared primitive
itself, `PendingValue<T>.TryConsumeIf(Predicate<T>, out T)`, needed because several candidate
systems must each ask "is this pending interact mine?" before one claims it. `VillagerSystem`
(trade: wheat → emerald) and `CowSystem` (feed: wheat → immediate breed eligibility) are the two
real consumers. Both share only the reach-check shape (already duplicated at the same tiny size as
`GroundMobCombat.ApplyPlayerMeleeAttacks`'s loop); their actual effects are unrelated enough that
no shared `Interaction`/`Transaction` type was justified. See §2's Interaction row and §4/§ below.

### Data pressure

**None.** `GolemSystem` iterates only its own store, same as every other system. No hot loop
processes heterogeneous categories together. Mob/NPC/boss counts remain small; nothing was
profiled because nothing suggested a cost worth profiling.

### Composition pressure

**None.** Golem — built specifically to be "complex" (phases, a second ability) — still fit
`IGroundMob`'s existing five members plus three of its own extra fields (`IsEnraged`,
`NextSlamTick`, `NextAttackTick`). No entity needed an arbitrary combination of Health + Inventory
+ Movement + Targeting + Effects + Persistence that didn't already fit an existing concrete
category shape.

### Maintenance pressure

**Real and specifically investigated this phase (see phase-xviii findings, Priority 3), still
below the extraction bar.** The `ActorInterest`-gated viewer-reconciliation loop is now duplicated
ten times (the nine from §7 plus `GolemSystem`). Checked directly against the brief's own evidence
bar: no measured drift bugs across the project's history, and no single gameplay change has yet
required touching more than one or two copies with different results needed in each. Still the
strongest concrete maintenance-pressure candidate in the project — worth a dedicated look if an
eleventh replicated category is added.

### Updated instance counts (targeting / wander)

- **Targeting**: still 2 identical instances (Zombie, Spider) + 2 differently-shaped single
  instances (Creeper, Enderman) + Golem's genuinely 4th shape (no retained field, re-scanned every
  tick). Golem was the real third-instance test §7 called for, and it came back negative — not
  extracted.
- **Wander**: still 2 instances (Cow, Villager). Golem does not wander (always either chasing or
  stationary pre-detection) — no third instance appeared.

---

## 9. Phase XIX Checkpoint

Phase XIX deliberately expanded outside the mob category for the first time — Minecart, a vehicle
with no AI at all — and added the project's first mob-sourced status effect (Spider's Poison
bite). Full detail: [phase-xix findings](history/phases/phase-xix-runtime-pressure-findings.md). Net result: the
category split held (Minecart is a third confirmation, after Bat and Golem, that
`IGroundMob`/its four helpers describe a runtime shape broader than "mob"), and cross-system
status-effect pressure resolved for free (`EffectSystem` needed zero changes to tick a
mob-applied effect).

### Entity categories beyond mobs (Priority 1)

`Minecart` — no AI, no targeting, no wander, no gravity, moves only via
`MinecartSystem.TryHandleInteraction` (the third `ActorInteract` consumer) then coasts under
friction. Still implements `IGroundMob` and reuses `GroundMobCombat`/`GroundMobMovement
.IsSupportedGroundCell`/`GroundMobLifecycle` unmodified (Minecart never gained gravity/step-up —
see Phase XXIX's scope note). No new runtime category emerged — see §1's Phase XIX
note. What remains untested: real bidirectional player-riding (a player controlling an entity's
movement in real time), deliberately deferred rather than built speculatively — see rejected
approaches in the phase findings.

### Cross-system pressure: status effects (Priority 2)

`SpiderSystem.TryApplyPoison` writes directly to the same `Player.Effects` dictionary
`EffectSystem.ApplyPendingIntent` writes to, and sends the same `MobEffectPacket`. `EffectSystem`
itself required zero changes — it already ticks whatever is in `Effects` regardless of source.
Confirmed: effects remain represented as `Player.Effects`/`ActiveEffect`, not a generalized
`StatusComponent`; the "does a mob need the same effect model" question resolved as "a mob can
already write into the player's model with the internal accessor it already had," not as
evidence a shared model needs to be built for mobs to also *receive* effects (no mob has needed
that yet).

### Priority 3 — continued monitoring

- **Targeting**: still 2 instances (Zombie, Spider). Minecart has none.
- **Wander**: still 2 instances (Cow, Villager). Minecart has none (inert unless pushed).
- **Viewer reconciliation**: an eleventh copy (`MinecartSystem`). Still below the evidence bar
  (no measured drift bugs, no forced multi-location edits), still the strongest concrete
  maintenance-pressure candidate in the project by raw count.
- **New**: the knockback/push impulse-plus-decay shape (Zombie's Phase XVI knockback,
  Minecart's Phase XIX push) now has two real instances — the first new candidate to reach that
  stage since targeting/wander in Phase XVII. Not extracted; watch for a third.

### ECS trigger conditions, checked again

- **Composition pressure**: none. Minecart fit `IGroundMob` with zero extra capability
  combinations beyond its own two fields (`VelocityX/Z`).
- **Data-oriented pressure**: none. No hot loop processes heterogeneous categories together;
  nothing was profiled because nothing suggested a cost worth profiling.
- **Maintenance pressure**: real only for viewer reconciliation, still below the bar. The
  push/knockback duplication is new but at only two instances — not yet pressure by this
  project's own standard.

---

## 10. Phase XX Checkpoint

Phase XX pressured runtime relationships (real player↔vehicle riding, not Phase XIX's one-shot
push), a third movement-validity model (Fish, swimming), and — for the first time — actually
resolved the viewer-reconciliation maintenance signal every prior checkpoint had flagged and left
open. It also carried out the `IGroundMob` naming review this document's own §7/§8/§9 kept
predicting was coming. Full detail:
[phase-xx findings](history/phases/phase-xx-runtime-relationships-findings.md).

### Riding: a real runtime relationship, resolved without a new primitive

`Player.RidingEntityId` / `Minecart.OccupantPlayerRuntimeId` — two plain nullable fields, one
writer (`MinecartSystem`), no `ActorLink`/`RiderRelationship`/`VehicleOccupancy` type. Every
lifecycle question (ownership, disconnect, destruction-while-occupied, late join, determinism) was
answered by that pair plus one narrow carve-out in `MovementSystem` (skip applying client
position while `RidingEntityId` is set — the same shape as its existing `IsDead`/`!IsInGame`
branches, not a new concept). Player movement did not become globally server-authoritative.

### Fish: the third movement-validity model

`FishSystem.TrySwim` requires the destination cell to *be* water — a categorically different rule
from ground's "supported, air above" or flight's "just air." All three remain separate, concrete,
2–4 line predicates. The only shared shape across ground/flight/swim is the heading-pick-and-retry
wander loop (Bat/Fish), still at 2 instances, below the extraction bar.

### Viewer reconciliation: resolved

`ViewerReconciliation.Sync` was extracted after a side-by-side audit of twelve consumers found
them byte-for-byte identical in shape. This required explicitly re-evaluating the standing
evidence bar (no measured drift bug, no forced multi-file edit) rather than reapplying it
unchanged — raw duplicate count, on its own, was treated as sufficient evidence once it reached
twelve. The helper takes no type parameter and no interface (not even for `IDamageableActor`,
since `Projectile` is a real consumer without that contract); relevance and both projection steps
are entirely caller-supplied. All twelve systems were refactored to call it; full suite passing
before and after confirms the refactor changed no behavior.

### `IGroundMob` → `IDamageableActor`

Renamed for the reasons §7–§9 had been accumulating evidence toward: ten implementers, several
neither "ground" nor "mob." The new name reuses this codebase's own existing "Actor" wire
vocabulary rather than reaching for `Entity`/`LivingEntity`. `GroundMobCombat`/
`GroundMobMovement`/`GroundMobLifecycle` were deliberately left unrenamed — `GroundMobMovement` is
correctly scoped; the other two are arguably just as stale as the old interface name was, but that
was out of this phase's explicit naming-review scope. Flagged for Phase XXI, not resolved.

### Store audit: no change

Twelve stores re-examined against three concrete questions (common capacity behavior, tooling
enumeration need, cross-store operations) — none appeared. A generic `Store<T>` was judged likely
to make a future real data-oriented migration *harder*, not easier, since such a migration would
replace stores as a concept rather than reuse a generic wrapper. Not extracted.

### ECS triggers, re-checked

Composition, data-oriented, and maintenance pressure were all checked against this phase's actual
gameplay (riding, swimming, a twelve-consumer audit). Only maintenance pressure crossed a bar —
viewer reconciliation — and it was resolved with a five-parameter static function, not a component
system or class hierarchy. No inheritance was introduced anywhere this phase.

---

## 11. Phase XXI Checkpoint — a real ECS, for a deliberately small slice

Every prior checkpoint in this document (§7–§10) evaluated ECS against an evidence bar and found
the bar unmet — not because ECS is wrong in the abstract, but because nothing measured in this
project ever showed the direct `*Store` model was the limiting factor. That evidence-gated
reasoning is preserved above, unedited, because it was correct given what was measured at the
time. Phase XXI does not overturn it with new measurements — it is a different kind of decision:
an explicit choice to build a real ECS runtime for a representative slice and evaluate it by use,
not by waiting for a specific actor count. Full detail:
[phase-xxi findings](history/phases/phase-xxi-ecs-foundation-findings.md); the ECS itself:
[`docs/ecs.md`](ecs.md); the ADR: [`docs/decisions.md` §106](decisions.md).

**Scope:** Zombie, Minecart, Projectile are now ECS-authoritative — their `Position`/`Health`/
`Velocity` live in `ComponentStore<T>`s inside a shared `EntityRuntime`, not in per-species
concrete classes. Skeleton, Cow, Creeper, Enderman, Bat, Spider, Villager, Golem, Fish — 9 species —
are untouched, still served by `IDamageableActor`/`GroundMobCombat`/`GroundMobMovement`/
`DespawnLifecycle` exactly as described in §1–§10 above. Every architectural finding this document
recorded about those 9 species remains accurate; nothing about their runtime shape changed.

### What this confirms from earlier checkpoints

- **§4's rejection of `BaseEntity`/`LivingEntity`/`MobBase` still holds.** The ECS did not
  introduce an inheritance hierarchy — `EntityId` is a plain generation-safe handle, and each
  migrated system still owns its own concrete behavior. §4's stated reasons (lifecycle differs per
  category, persistence differs per category, replication differs per category) remain true of the
  migrated slice too: Projectile still has pure-age lifetime while Zombie/Minecart still have
  visibility-reset lifetime (now expressed as one shared component, `DespawnTracking`, that
  Projectile deliberately does not carry — see `docs/ecs.md`'s components table).
- **§3's `PositionComponent` candidate is now real** — for 3 of 12 categories. The evidence this
  document asked for ("does a future system need to process positions together") never actually
  materialized as the trigger; what materialized instead was a scope decision (Phase XXI's brief)
  to build it and observe. `Position`/`Velocity` are now genuinely shared structs across
  Zombie/Minecart/Projectile, collapsing what used to be 3 separate float-triplet properties per
  concrete class into one component definition.
- **§4's `GenericEntityStore<T>` candidate is resolved, not by generalizing the old `*Store`
  pattern, but by replacing it.** The "closest-to-justified rejected candidate" from §4 predicted a
  sixth `List<T>`-shaped store would be the Phase XVII trigger; instead, the migrated slice's 3
  stores were deleted outright and replaced by `EntityWorld`'s allocator plus per-component
  `ComponentStore<T>`s. The 9 unmigrated species' stores are untouched and still exist exactly as
  §4 described them.

### Capability matrix and pressure-candidate sections above

§2's capability matrix and §3's pressure candidates were written against the pre-ECS runtime and
remain accurate as a description of the *unmigrated* 9 species. They are not being rewritten for
this phase — a reader comparing Zombie's old `PARTIAL` velocity entry (§2) against its new
`Velocity` component should understand the matrix describes the state as of Phase XX, same as every
earlier phase's checkpoint left prior sections' phase-dated claims in place (see the Phase XX
banner note at the top of this document, which follows the same pattern for `IGroundMob` →
`IDamageableActor`).

### ECS triggers, re-checked

This phase did not pass a new data-oriented, composition, or maintenance pressure threshold before
building the ECS — see [Why this phase exists](history/phases/phase-xxi-ecs-foundation-findings.md#why-this-phase-exists)
in the findings doc for the honest framing. The benchmarks recorded there show zero measured
allocation regression and no dramatic win either; the decision to build was a deliberate scope
choice, not a §5-style evidence trigger. §5's trigger conditions remain the right bar for whether
the *next* species should migrate, or whether archetypes/a job scheduler are ever justified — none
of those thresholds were crossed this phase either (see the findings doc's Final Question 11).

---

## 12. Phase XXII Checkpoint — the ECS roster doubles, damage dispatch becomes capability-oriented

Phase XXII migrated three more species — Cow, Skeleton, Spider — chosen specifically to pressure
different gameplay shapes than Phase XXI's trio: a passive/breeding actor, a ranged actor that
spawns another ECS actor, and a melee actor with a feature-specific post-hit consequence (poison).
Full detail: [phase-xxii findings](history/phases/phase-xxii-ecs-roster-consolidation-findings.md);
the ECS itself: [`docs/ecs.md`](ecs.md); the ADR: [`docs/decisions.md`](decisions.md).

**Scope:** ECS-authoritative roster is now Zombie, Minecart, Projectile, Cow, Skeleton, Spider —
six species. Legacy `IDamageableActor` roster is now Creeper, Enderman, Bat, Villager, Golem,
Fish — six species. Every architectural finding this document recorded about those six legacy
species (§1–§10) remains accurate; nothing about their runtime shape changed.

### What crossed a real trigger this phase

- **The Phase XXI cross-category dispatch trigger fired.** `docs/ecs.md`'s Phase XXI text
  explicitly said a third ECS-damageable category would be the trigger to replace
  `ProjectileSystem`'s hardcoded `if (_zombies.Owns) else if (_minecarts.Owns)` chain with a real
  seam. Cow/Skeleton/Spider's migration produced five ECS-damageable species besides Projectile
  itself, and the chain was replaced with `DamageDispatch` — a small, fixed, registered-handler
  list built once at composition-root time, not a generic event bus. See `docs/ecs.md`'s
  Cross-category dispatch section.
- **The `GroundMobCombat`/`DamageableActorCombat` retirement gate was re-evaluated, deliberately
  not fired.** Roster parity (6 migrated / 6 legacy) was reached, but the trigger was never "reach
  parity" — it was "migrating the stragglers costs less than maintaining two helpers." The 6
  remaining species (fuse+explosion, damage-triggered aggro+teleport, 3D flight, trade interaction,
  boss phases, swim) each still carry genuinely distinct behavior, the same evidence this document
  already built up across §7–§10. No maintenance-cost signal (a bug fixed in one helper and missed
  in the other) has appeared either. See `docs/ecs.md`'s Retirement gate section for the full
  per-species accounting and the updated trigger condition.

### What this confirms from earlier checkpoints

- **§4's rejection of a generic entity hierarchy still holds**, now tested against a passive mob
  (Cow), a ranged mob that composes with another ECS system (Skeleton→Projectile), and a
  melee mob with an *outgoing* status effect (Spider→Player.Effects) — three more materially
  different shapes, none of which needed an inheritance layer to fit the same `EntityId` +
  component-store model Zombie/Minecart/Projectile already used.
- **"No fake universal components" (§3's original framing) held again.** Cow/Skeleton/Spider do not
  carry a `Velocity` component — none of the three ever needs one, unlike Zombie (knockback),
  Minecart (rolling motion), or Projectile (ballistic integration). Attaching it anyway "for
  consistency" was explicitly avoided.
- **Skeleton→Projectile is a real ECS-to-ECS composition test, and it stayed narrow.** No
  `SkeletonProjectileComponent`, no ability framework — `SkeletonSystem` calls
  `ProjectileSystem.TrySpawnFromActor` directly, the same call it made before migration, just with
  an ECS-derived owner id. This is the concrete answer to whether ECS-backed gameplay composes
  across systems without restoring concrete-type coupling: yes, and the composition point didn't
  need to grow to support it.

### ECS triggers, re-checked

Composition and data-oriented pressure remain unmeasured/unmet — the same honest framing as Phase
XXI applies (see that phase's Final Question 6 and this phase's own findings doc). Maintenance
pressure crossed one real bar (cross-category damage dispatch, above) and was resolved with the
smallest design that eliminated the type-switch growth, not a generic framework. Archetypes remain
unjustified — six ECS-damageable species, still zero measured allocation regression at steady
state (see the findings doc's benchmark section).

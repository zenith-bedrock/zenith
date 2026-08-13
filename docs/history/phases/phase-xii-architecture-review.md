# Phase XII — Architecture Consolidation & Runtime Evolution

## How this review was done

Audited `src/zenith` directly (no framework assumptions going in): every file under
`Gameplay/`, `Gameplay/Systems/`, `Player/`, `Protocol/`, `World/`, `Session/`, `Server/`, plus the
`Zenith.Diagnostics` library. Cross-referenced against the actual repetition counts (grep, not
impression) before proposing anything. The question asked throughout was not "does this look like a
framework" but "is there now a second, third, or fourth real consumer of the same shape, and does
merging them reduce complexity without a performance or clarity cost."

Three consolidations were judged to clear that bar and were implemented (with tests, and full-suite
regression runs after each). Several other candidates were seriously considered and rejected — the
reasons are recorded below, not just the verdicts.

---

## 1. Player State Evolution

### What exists today

`Health` (`HealthState`, owned via `PlayerDamage.Apply`), `Hunger`/`Exhaustion` (plain fields, owned
by `HungerSystem`), `Armor` (4 slots inside `PlayerInventory`, owned by `InventorySystem`/
`ArmorMitigation`), `Effects` (`Dictionary<EffectType, ActiveEffect>`, owned by `EffectSystem`),
`Experience` (`ExperienceLevel`/`ExperiencePoints`, owned by `Player.AddExperience` and mob-kill
credit), `Inventory` (`PlayerInventory`, owned by `InventorySystem`).

### Repetition found

Six independent call sites (`PlayerDamage` ×2, `ResourcePacksSessionHandler`, `EffectSystem`,
`HungerSystem`, `MovementSystem`) were each hand-computing the same wire projection —
`SendDefaultAttributes(rid, health, hunger, level, PlayerExperience.Progress(level, points))` —
every time any one of Health/Hunger/Experience changed. This was not hypothetical: Phase XI.4 had to
edit all six by hand to add the level/experience parameters, and a XI.5-shaped future vital would
require editing all six again.

### Decision: add a wire-projection method, not a state model

**Implemented:** `EntityProtocol.SendPlayerAttributes(Player player)` — reads the player's current
Health/Hunger/Experience and sends the attributes packet. All six call sites now call this instead of
reconstructing the projection. A seventh and eighth call site (`ZombieSystem`/`SkeletonSystem`'s
kill-XP credit) also collapsed onto it via the `MobKillReward` extraction (see §5).

**Rejected: `PlayerVitals`/`PlayerStateSnapshot`/`PlayerDataModel` as an aggregate object.** The
brief's own examples were offered as "examples, not objetivos," and the audit confirms why a snapshot
struct is the wrong shape here: Health, Hunger, Effects, and Experience have deliberately different
lifecycles that a earlier phase reasoned through individually —
Hunger and Effects are explicitly *not* persisted (XI.1 and XI.3 findings), Armor and Experience *are*
persisted but through different keys with different write cadences (§4). A `PlayerVitals` struct
aggregating fields with different persistence/replication rules would either (a) force a decision
about what belongs in it that contradicts an already-reasoned-through choice, or (b) become a loose
grab-bag with no behavior of its own — worse than the six independent fields it replaced. The actual
repeated *operation* was "project current vitals to the wire," not "the vitals are one object" — so
the fix targets the operation.

---

## 2. Replication Architecture

### What exists today

`EntityProtocol` (516 lines) and `InventoryProtocol` (360 lines) are per-session facades, already
decomposed by domain (Entity/Inventory/World/Chat/Ui/Skin/Login/...). `PlayerVisibility` sequences
join/leave/health/swing/emote fan-out. Five standalone `*Fanout` static classes exist:
`ArmorFanout`, `BlockCrackFanout`, `BlockSoundFanout`, `ChestLidFanout`, `FloorDropFanout`.

### Repetition found (real, measured)

The recipient loop `foreach (peer in online) { if (excluded) continue; peer.Session.Protocol.X.SendY(...); }`
appears **13 times across 6 files** (`ArmorFanout` ×1, `BlockSoundFanout` ×1, `ChestLidFanout` ×1,
`BlockCrackFanout` ×3, `BlockEditSystem` ×1, `PlayerVisibility` ×6). This is the single most-repeated
raw shape in the codebase — more instances than the intent mailboxes (§3) had before consolidation.

### Decision: keep concrete — evaluated and rejected a generic fan-out primitive

A generic `PeerFanout.ToOthers(online, subject, Action<Player> send)` was designed and rejected for
two concrete reasons, not reflexive avoidance:

1. **The recipient filter genuinely varies.** `BlockCrackFanout`/`BlockSoundFanout`/`ArmorFanout` gate
   on "every other InGame peer" (no visibility check). `ChestLidFanout` gates on
   `peer.Chunks.Knows(chunkX, chunkZ)`. `FloorDropFanout` and the mob systems gate on a
   per-actor replicated/interest set (`ActorInterest`, Phase VI's proven chunk-knowledge model). A
   generic primitive would need a filter parameter anyway — at which point it is barely shorter than
   the current `if` line it replaces, while adding one more layer to read through.
2. **Allocation risk in an active hot path.** `BlockCrackFanout.UpdateSpeed`/`Start`/`Stop` and
   `BlockSoundFanout.Hit` run on every tick a player is actively digging (`BlockDigSystem` calls them
   per continued dig, not just on state transitions). A `send` callback capturing per-call arguments
   (block coordinates, break ticks) is a closure — a heap allocation per call in a path this codebase
   has otherwise kept allocation-free (per the diagnostics module's own stated goal and Phase
   VI/VII's measured allocation-reduction work). A non-capturing static-lambda-plus-state-struct
   version avoids the allocation but is materially harder to read than the five lines it replaces.
   Trading a proven zero-alloc hot path for marginal DRY-ness fails the "maintains performance"
   criterion.

**Verdict: still the best solution.** Each fan-out's filter is a real behavioral difference, not
incidental duplication, and the loop body itself is five lines. Revisit only if a *sixth* filter
shape appears that duplicates one already listed above verbatim (not just "another fan-out"), or if
profiling ever shows this loop family costing tick budget.

`EntityProtocol`/`InventoryProtocol` size: not flagged. Both are single-domain facades that grow
linearly with new packet types (one method per packet), same shape since Phase I. No internal
repeated pattern was found inside them beyond the fan-out loops already addressed above.

---

## 3. Intent / Command Flow

### What exists today (before this phase)

`Player` had grown to **15 hand-written Submit/TryConsume pairs** (~958 lines total, roughly 20% of
the file was this exact boilerplate), covering three shapes:

- **Overwrite-latest flag** (no payload): Attack, Projectile, Eat, Respawn, SpawnReady — 5 instances.
- **Overwrite-latest value**: GameMode, Effect — 2 instances (GameMode uniquely reports the *current*
  value instead of `default` when nothing is pending).
- **Bounded FIFO queue**: BlockEdit, InventoryStack, WindowIntent, Chat — 4 instances (each with its
  own `Queue<T>` + lock + capacity constant).

(Dig and MovementInput were audited too but excluded from consolidation — see below.)

### Decision: extract the storage mechanism, not an intent bus

**Implemented** three small internal types in `Player/PendingMailbox.cs`:

```
PendingSignal        — overwrite-latest flag (5 consumers)
PendingValue<T>       — overwrite-latest value, optional fallback-when-empty (2 consumers)
PendingMailbox<T>     — bounded FIFO (4 consumers)
```

11 of the 15 pairs now delegate to one of these in one line each. Every gameplay guard (`if (IsDead)
return;`, `if (!IsDead) return;`, the window-intent's `Close`-during-death exception) stays a plain
`if` in `Player` around the call — the primitives know nothing about gameplay, dead/alive state, or
what an "attack" is.

**Naming**, revised mid-implementation per explicit review: not `*Intent*` — "intent" already names a
specific Zenith concept (a bounded, gameplay-typed player action). These are the plumbing underneath
an intent, so they're named as mailboxes (a producer submits, one consumer drains) instead, avoiding
the false impression of a routing/dispatch layer.

**Excluded from consolidation:**
- **`Dig`** (`SubmitDigStart`/`Abort`/`Activity`/`ActivityForActive`) — the queue is entangled with
  two extra pieces of side-state (`_provisionalDig`, `_suppressedDigAuthorization`) read/written under
  the same lock, plus a linear `CancelPendingDigStart` scan. Forcing this into a generic queue would
  either leak that side-state through the primitive's API or leave it half-migrated. Left concrete.
- **`MovementInput`** — has a third operation (`TryPeekMovementInput`, non-consuming) the mailbox
  shapes don't have, and only one instance exists. Not enough repetition to justify a fourth shape.

**Synchronization:** kept as one uncontended `lock (object)` per mailbox — identical to every
hand-written version before it. Call volume is one `Submit` per relevant input packet (tens/second at
most per player) against one `TryConsume` per GameLoop tick (20/second); nothing in the diagnostics
tick budget suggests this is measurable. Recorded as a future re-evaluation point only: if profiling
ever shows contention here (many concurrent players, many intents/tick, or mailbox allocation
pressure), the next step would be a bounded custom structure or SPSC ring — not attempted now,
because there is no evidence for it yet.

**Validation:** 11 new leaf tests (`PendingMailboxTests.cs`) cover only the primitive contract
(submit→consume, latest-wins, fallback-when-empty, FIFO order, overflow-rejects-newest,
drain-then-resubmit) — no gameplay rule was moved into these tests. Full suite green before and after
at every step (694/694 final).

### Other intent-flow repetition noted, not acted on

The **command → `SubmitEffect`/`SubmitGameMode` overwrite-latest** shape used by `/effect` and
`/gamemode` is now naturally consistent since both route through `PendingValue<T>` underneath — no
further action needed; this was a side effect of §3's consolidation, not a separate change.

---

## 4. Persistence Model

### What exists today

Three `IChunkStorage` key families for player-scoped state: `inv:{uuid}` (main 36-slot bag),
`ar:{uuid}` (4 armor slots, added Phase XI.2), `pd:{uuid}` (pose + GameMode +, as of Phase XI.4,
experience level/points — `PlayerDataBlob` v1→v2). Chest and world overlay data use position-keyed
entries (`ct:`, `ov:`), a different concern entirely. Hunger and Effects are deliberately *not*
persisted (explicit, documented decisions from XI.1 and XI.3).

### Investigated: `PlayerStorageEntry` (one blob for all player state)

**Rejected.** Two concrete reasons:

1. **Write cadence mismatch.** Inventory changes on nearly every inventory-touching action (many
   times a minute during active play). Playerdata (pose/mode/XP) changes on gamemode switch, mob
   kill, and quit — much less frequently. Armor changes only on equip/unequip. Merging these into one
   blob means every inventory mutation would force a rewrite of pose/mode/XP data too (and vice
   versa) — pure I/O amplification for LevelDB, with no compensating benefit.
2. **The versioned-blob pattern is already proven and cheap to extend.** `SlotBlob` went v1→v2 once
   (Phase pre-XI, tool-vs-block migration) and `PlayerDataBlob` went v1→v2 in Phase XI.4 (added XP).
   Both migrations were a few lines: bump `Version`, append fields, keep the old reader path for
   back-compat. Extending an existing key costs less than either merging keys (rewrite + migrate every
   existing save) or keeping fragmentation (the current state, which already works).

**Verdict: still the best solution.** Three keys, each matched to a genuinely different write
cadence, each independently versioned and migrable. Revisit only if a *fourth* independent-cadence
concern needs its own key and the key-management overhead itself (not the blob format) becomes the
pain point — no such signal exists today.

Not evaluated as a persistence problem: LevelDB itself, `WorldStorageKeys`' string-prefix scheme, or
`IChunkStorage`'s async signature — all stable, no repetition signal, out of scope for "does
abstraction help now."

---

## 5. Entity / Actor Runtime

### What exists today

`Zombie`/`ZombieStore`, `Skeleton`/`SkeletonStore`, `Projectile`/`ProjectileStore` — each a tiny
concrete class (`EntityId`, `RuntimeId`, position, `IsActive`, `Remove()`) plus a `List<T>`-backed
store (`TryAdd`/`Remove`/`Active`). `FloorDropStore`/`ChestStore`/`GravityPendingStore`/
`FallingBlockStore` are structurally different (dictionary/spatial-keyed, soft-capped) — not part of
this comparison; they were never claiming to be the same shape.

`Projectile.cs`'s own doc comment already flags exactly this question as deliberately deferred:
*"This storage shape is intentionally scaffolding: it records pressure alongside
ZombieStore/FallingBlockStore, rather than defining a reusable actor contract."*

### Repetition found

`Zombie` and `Skeleton` are near-identical in shape (same 6 fields, same `ApplyDamage`/`Remove`
methods). `ZombieStore` and `SkeletonStore`/`ProjectileStore` are near-identical
`List<T>` + `TryAdd`/`Remove`/`Active` wrappers (~10-15 lines each).

**A real, freshly-introduced duplication was found and fixed**: Phase XI.4 had added an *identical*
`AwardKillExperience` private method to both `ZombieSystem` and `SkeletonSystem` — not about the
entities' shape, but about the player-side kill-reward step both systems needed.

### Decision: extract the kill-reward duplication; do not touch entity/store shape

**Implemented:** `Gameplay/MobKillReward.AwardExperience(source, amount, online)` — one static method,
two callers (`ZombieSystem`, `SkeletonSystem`), zero mob-framework surface. This is a "small shared
helper," explicitly not an entity abstraction.

**Rejected: a shared `LivingActor` base/composed value for Zombie/Skeleton, or a generic
`ActiveActorStore<T>`.** Two entity types sharing a shape is a weak signal on its own — this codebase
has held that line deliberately since the Phase III mob-behavior findings ("Cada comportamento
continua concreto") and nothing observed in this audit contradicts that reasoning: no difficulty was
found adding Skeleton after Zombie (the actual test of "does composition repeat painfully"), no third
mob type exists to triangulate a real contract from, and Projectile/FallingBlock differ enough
(velocity-driven, no melee/ranged AI) that a shared base would need to be either too thin to matter or
padded with unused members for the outliers. The store wrapper (~10-15 lines ×3) is the same
calculus: real but trivially small duplication, not worth a generic type yet.

**Verdict: stores/concrete-actors are still adequate.** ECS was not reconsidered because no evidence
changed since the original ECS-feasibility phase: still no composition explosion, still no measured
allocation/iteration cost tied to per-mob-type code (the fixed diagnostics timings for `zombie`/
`skeleton`/`projectile` show no anomaly). The trigger for revisiting this is explicit: a third
"living, AI-driven, meleeable-or-shootable" actor type. Two is a coincidence; three is a pattern.

---

## 6. Diagnostics & Performance

### What exists today

`Zenith.Diagnostics` (ring buffer, fixed metrics, incident snapshots) stayed untouched by every phase
except for adding one `Timing` entry per new `IGameSystem` (now 16 fixed system timings) — this is
itself evidence the diagnostics model already generalizes correctly: each phase's addition cost
exactly one dictionary line, never a diagnostics-side change.

### Findings

- No new hot-path allocations were introduced by this phase's own changes: `PendingSignal`/
  `PendingValue<T>`/`PendingMailbox<T>` use the same lock+field mechanism as the code they replaced
  (verified: no new `Action<>`/closure/delegate was introduced anywhere in the consolidation).
  `SendPlayerAttributes`/`MobKillReward.AwardExperience` are direct method calls, not virtual dispatch.
- The rejected fan-out generic (§2) was rejected specifically *because* it would have introduced
  closure allocations into an already-hot, already-measured path — this is the one place this phase
  found a real perf-vs-DX tradeoff and came down on the performance side.
- No allocation, tick-cost, or GC anomaly was found elsewhere in the audit. `ServerRuntimeDiagnostics`
  already tracks `runtime.allocations.thread`/`runtime.gc.gen{0,1,2}`/`tick.over-budget` — nothing in
  this phase's changes needed a new metric, and no existing metric flagged a problem to chase.

**Verdict: diagnostics module needs no evolution.** It has correctly stayed a fixed, cheap, additive
registry through 12 phases of growth. This is the diagnostics-module equivalent of §4's persistence
verdict: the boundary already generalizes, so there's nothing to build.

---

## Also audited, no action needed

- **`EventBus`** — still exactly what its own doc comment says: login/quit only, one composition-root
  subscriber (`PlayerPresenceAnnouncer`), deliberately without unsubscribe/priority/cancellation. No
  phase since it was introduced has needed a second domain event, so there is no pressure to expand
  it into anything resembling a public event bus. Correctly still small.
- **Command system (`CommandCatalog`/`CommandRuntime`)** — used identically by `/gamemode`, `/effect`;
  no repetition signal beyond what §3 already addressed (both route through `PendingValue<T>` now).

---

## Summary — what matured, what didn't

| Area | Verdict |
|---|---|
| Player vitals wire projection | **Matured.** `EntityProtocol.SendPlayerAttributes` implemented — 8 call sites deduplicated. |
| Intent/command handoff mechanism | **Matured.** `PendingSignal`/`PendingValue<T>`/`PendingMailbox<T>` implemented — 11 of 15 Player pairs deduplicated, with dedicated primitive tests. |
| Mob-kill XP credit | **Matured.** `MobKillReward` implemented — a fresh (Phase XI.4) duplication caught and fixed before it could spread further. |
| Replication fan-out loop | **Still concrete.** Real repetition (13 instances) but genuinely different filters plus real hot-path allocation risk; rejected with reasons, not by default. |
| Player state persistence | **Still concrete.** Three keys matched to three write cadences; versioned-blob extension already proven cheap twice. |
| Entity/actor runtime | **Still concrete.** Two similar mob types is not yet three; the store/entity shapes stay hand-written until a third living actor exists. |
| Diagnostics | **Still concrete.** Already a correctly-generalized additive registry; nothing to build. |
| EventBus | **Still concrete.** No second domain consumer has appeared. |

**Answer to "what matured?"**: the *mechanism* layers — how a network thread hands work to the
GameLoop (§3), and how the GameLoop reports a player's current vitals to the wire (§1) — had
genuinely outgrown hand-written repetition and are now small, tested, ownership-preserving
primitives. The *domain* layers — replication filtering, player-state persistence, and actor
composition — have not: each still has as many genuinely distinct cases as concrete implementations,
not yet a proven common shape. Zenith leaves Phase XII with three small, justified primitives and zero
new frameworks — the codebase is more consolidated, not more general.

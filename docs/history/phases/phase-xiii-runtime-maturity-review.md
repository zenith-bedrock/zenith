# Phase XIII — Runtime Hardening & Developer Experience

## How this review was done

Audited `src/zenith` and `src/zenith.Tests` directly across the seven areas the brief named, in
order. Every change below was made only after finding real evidence (a repetition count, a
measured allocation number, a concretely oversized method) — not from anticipation. Where a
sub-area turned up no actionable finding, that is recorded as a conclusion, not skipped.

Build/test validated after every change (`dotnet build`, `dotnet test`) — full suite green
throughout (694/694, unchanged pass count since no new gameplay tests were needed).

---

## XIII.1 — Test Architecture Review

**Found:** `IntentTestFixture` (plus its three small support types `SilentLogger`,
`RecordingRakNetServer`, `StubSessionHandler`) is the de facto shared "create Player + World +
Session" factory — used by 30 of 87 test files. It was defined at the top of
`IntentContractTests.cs`, a 2852-line file about a specific test class, not about being a shared
harness. The other 57 files correctly don't use it (they're pure packet encode/decode tests with no
gameplay dependency) — no repetition problem there.

**Decision: extract, don't build a test framework.** Moved the four types verbatim into
`IntentTestFixture.cs`. Zero behavior change (same types, same members, same `file`/`internal`
visibility), pure discoverability fix for a dependency 30 files share. Did not introduce a
`TestPlayerFactory`-style builder API, mocks, or any new abstraction — the fixture's existing shape
was already right, it just lived in the wrong file.

---

## XIII.2 — Composition Root Review

**Found:** `ZenithServer`'s constructor was ~120 lines, interleaving logger/config setup, palette
loading + 13 boot-contract `Require` calls, world/store construction, and **16 `gameLoop.Register`
calls** with real ordering comments ("Movement before Block/Inventory...", "After Inventory: eating
also mutates...") that must stay adjacent to the calls they explain.

**Decision: Caso B — group the registrations, don't touch anything else.** Extracted two private
static methods:

- `RegisterEarlySystems` — the 4 systems needing only `PlayerManager` (before palettes exist).
- `RegisterWorldSystems` — the 12 systems needing world/palettes/recipes, in their exact original
  order with every comment preserved; returns only `GravitySystem` (the sole handle the constructor
  still needs afterward, for graceful-shutdown flush).

Did not extract palette loading, storage resolution, or RakNet setup — those are already
single-purpose blocks, not lists that grow with every gameplay phase the way system registration
does. Did not introduce a DI container, service locator, or generic "module" concept.

---

## XIII.3 — Protocol Boundary Review

**Found:** No oversized or duplicated methods in `EntityProtocol` (516 lines), `InventoryProtocol`
(360 lines), or `WorldProtocol` (293 lines) themselves — every method is one packet, sized to that
packet's field count, consistent `SendX` naming throughout. No gameplay-decision leakage into
Protocol (`grep` for `GameMode.`/`IsDead`/vital-threshold comparisons inside `Protocol/*.cs` found
nothing) — the `Gameplay decision → Protocol projection → Packet` boundary holds.

Two session-handler methods were genuinely large: `LoginSessionHandler.HandleLogin` (162 lines) and
`InGameInventoryHandler.HandleItemStackRequest` (161 lines, before this phase's edit).

- **`HandleLogin` — left alone.** It's one linear sequence progressively building a single `Player`
  object (auth-type checks → identity parsing → skin → profile → registration → persistence
  hydrate → response), with early-return guards at each step. Splitting it would mean threading that
  half-built state through several methods for no readability gain — this is length from breadth of
  a single sequential concern, not mixed concerns. No repetition, no history of being hard to
  change. Flagged and rejected, not silently skipped.
- **`HandleItemStackRequest` — extracted.** The inner `foreach (action in request.Actions)` loop
  (~65 of the 161 lines) was a **pure function in disguise**: wire action in, domain
  `InventoryStackAction` out, or a bool failure — no state threading, no dependency on anything
  outside the one `action` parameter. Extracted as `TryMapAction(in DecodedStackRequestAction,
  out InventoryStackAction) : bool`, following the codebase's existing `TryX` convention. The outer
  method now reads as request-level orchestration (validate → map each action → creative/session
  checks → submit), not a 161-line block. Verified against the full ISR/inventory test suite
  (`InventoryRearrangeTests`, `InventoryDuplicationRegressionTests`, `IntentContractTests`'s ISR
  cases, `ArmorTests`) — all still pass.

A deeper pipeline-specific review of the inventory boundary (`InventoryStackAction`'s
intent/command/result separation, validation-layer split, ownership map, persistence consistency,
and forward pressure from trading/crafting/enchantments) was requested separately and is its own
document: [`docs/phase-xiii-inventory-action-review.md`](phase-xiii-inventory-action-review.md).
No code changes were made from that review — it is analysis only, pending approval.

---

## XIII.4 — Diagnostics & Observability Review

Four questions from the brief, answered honestly rather than instrumented reflexively:

| Question | Status |
|---|---|
| Which system costs most tick? | **Already answered.** 16 fixed per-system `Timing` metrics plus `/diagnostics tick` (`DescribeTimings`) already rank them. No gap. |
| Which player generates most intents? | **Not answered, correctly.** No pending decision depends on this; per-player dynamic metrics were explicitly rejected in Phase XI's findings and nothing here overturns that. |
| Which packets cost most? | **Not answered.** `network.packets.sent`/`network.packet-bytes.sent` are aggregate only. No per-packet-type cost problem has ever been observed or reported — adding a breakdown now would be instrumentation for a question nobody has needed answered yet. Deferred. |
| Where do allocations appear? | **Answered well enough by the existing benchmark harness** (`RuntimeLoadHarness`, `--zombie-behavior`) rather than by adding a new diagnostics metric — see XIII.5. |

**Decision: no new metric added.** Every fixed metric that exists answers a question someone
actually asked in a past phase; nothing in this audit produced a *pending decision* that a new
metric would resolve. The brief's own rule — "add a metric only when a real question exists and a
decision depends on it" — was satisfied by *not* adding one. If per-system allocation ever becomes
the pending question (see XIII.5's future-trigger note), the pattern to extend is already proven:
copy the fixed-`Timing`-per-system shape that already tracks tick cost.

---

## XIII.5 — Allocation & Memory Audit

**Method:** static code review for LINQ/closures/boxing in per-tick paths, then the existing
`zenith.Benchmarks` `RuntimeLoadHarness` (`--runtime-load --zombie-behavior`) for real numbers — not
guessed ones.

**Code review findings:**
- The GameLoop tick path itself (`GameLoop.TickOnce`, `MovementSystem`, `HungerSystem`,
  `EffectSystem`, `InventorySystem`, `EquipmentSystem`, `ChunkStreamSystem`) has **zero LINQ calls**
  — confirmed by grep across `Gameplay/Runtime` and the systems most players touch every tick.
- `ZombieSystem`/`SkeletonSystem`/`ProjectileSystem` use `.Active.ToArray()` every tick (snapshot
  before iterating, since the tick body may remove from the same store) plus `.Where`/`.Any`/
  `.FirstOrDefault`/`.OrderBy` for target acquisition and replicated-set pruning. This is real,
  measurable per-tick allocation proportional to active-actor count.
- `PlayerVisibility.AnnounceJoin` uses LINQ (`.Where().ToArray()`) but only runs once per player
  join — not a tick-rate path.
- No boxing pattern was found (no `object`-typed hot-path parameters, no non-generic collections of
  value types in the tick path).
- This phase's own additions (`PendingSignal`/`PendingValue<T>`/`PendingMailbox<T>`,
  `SendPlayerAttributes`, `MobKillReward`, `TryMapAction`) introduce **no new allocations**: no
  closures, no LINQ, direct method calls only.

**Measured (not guessed):** `dotnet run --project src/zenith.Benchmarks -c Release --
--runtime-load --zombie-behavior --actors 1000 --actor-players 10 --ticks 200`

| Mode | avg tick | alloc/tick | Gen0/1/2 (200 ticks) |
|---|---|---|---|
| idle | 2.515ms | 154,180 B | 3/0/0 |
| direct (combat) | 1.356ms | 208,082 B | 4/0/0 |
| obstacle (pathing) | 1.171ms | 213,937 B | 5/0/0 |

At 1000 concurrent zombies (the codebase's own established stress target from earlier
interest-scaling phases), average tick cost is **1–2.5ms against a 50ms budget**, and only 3–5 Gen0
collections occur across 200 ticks (10 seconds) — cheap, non-disruptive collections, no Gen1/Gen2
pressure at all. The `.ToArray()`/LINQ allocation in the mob systems is real but **not a measured
problem**.

**Decision: no optimization applied.** This is a "measured and found fine" verdict — exactly as
valid an outcome as finding and fixing something, per the brief. Object pooling, a custom allocator,
or replacing the array-snapshot pattern would be optimizing a number that isn't costing anything
today. Documented as a **future trigger**: if a scale target beyond 1000 actors is ever adopted, or
if `tick.over-budget` ever fires in production, re-run this exact benchmark first — the harness and
the numbers to compare against already exist.

---

## XIII.6 — Internal API / Naming Review

Two live naming issues were caught and corrected *during* this phase (both via direct review
feedback, addressed immediately rather than deferred):

- `EntityProtocol.SendVitals` → **`SendPlayerAttributes`**. "Vitals" reads as a gameplay model;
  the method is a Bedrock `UpdateAttributes` wire projection (Health/Hunger/Experience), same
  category as the packet it wraps. Renamed at all 8 call sites.
- `Player.PendingQueue<T>` → **`PendingMailbox<T>`**, and the three primitives' shared doc comment
  now explicitly states they are not an intent bus. "Intent" already names a specific Zenith concept
  (a bounded, gameplay-typed player action); these types are the plumbing underneath one.

**Swept for further issues, found none requiring change:** `Fanout`/`Store` suffixes are used
consistently and mean what they say (`ArmorFanout`, `ChestLidFanout`, `FloorDropFanout` all push
replication; `ZombieStore`, `ChestStore`, `FloorDropStore` all own storage). `*Intent` types
(`EffectIntent`, `BlockEditIntent`, `DigIntent`, `InventoryStackIntent`, `InventoryWindowIntent`) are
named consistently for the one concept that name should mean. `MobKillReward` and
`PlayerExperience` are named for exactly what they do and nothing more. `Send*` protocol methods are
uniform across all three protocol facades.

---

## XIII.7 — Architecture Decision Review

Re-asked every question Phase XII closed, checking specifically for new evidence produced by this
phase's own work (no new gameplay features were added in XIII, so the honest expectation going in
was "nothing changed" — confirmed):

| Question | Phase XII verdict | New evidence this phase? | Current verdict |
|---|---|---|---|
| ECS | Not yet — no composition explosion, no 3rd similar mob | None — no new entity type added | **Unchanged.** |
| Entity hierarchy | Two similar mobs isn't three | None — still Zombie/Skeleton only | **Unchanged.** |
| Replication (fan-out) abstraction | Filters genuinely differ; hot-path alloc risk | None — no new fan-out added; XIII.5's benchmark run independently confirms current mob-system allocation is not a tick-budget problem | **Unchanged, independently reconfirmed.** |
| Persistence keys | 3 keys, 3 write cadences, each justified | None — no new persisted concern added | **Unchanged.** |
| Event system | Login/quit only, one subscriber | None — no second domain consumer appeared | **Unchanged.** |

No decision was altered. This is itself informative: a hardening/DX phase that touches composition
root, protocol boundary, and test infrastructure without disturbing any of Phase XII's domain-layer
verdicts is evidence those verdicts were sound, not evidence they weren't tested.

---

## Definition of Done

- ✅ This document.
- ✅ Problems found: test-harness discoverability, composition-root registration noise, one
  oversized-but-splittable handler method. All three fixed.
- ✅ Improvements applied only where justified: 3 small, low-risk, behavior-preserving refactors
  (`IntentTestFixture.cs` extraction, `ZenithServer` registration grouping, `TryMapAction`
  extraction). Two naming fixes. Zero new abstractions, zero new frameworks.
- ✅ Validated: `dotnet build` clean, `dotnet test` 694/694 after every change; one real benchmark
  run (`RuntimeLoadHarness --zombie-behavior`) produced the allocation numbers XIII.5's verdict
  rests on.
- No ADRs were needed — none of the changes altered a prior architectural decision, only its
  presentation (file location, method boundaries, names).

## What matured?

**"Test setup already had its primitive — it just needed to be found."** `IntentTestFixture` was
never missing; it was misplaced. **"Composition root needed organization, not restructuring."**
Grouping registrations by dependency phase (`RegisterEarlySystems`/`RegisterWorldSystems`) made the
constructor readable without changing what it does or how systems depend on each other. **"The
protocol boundary was already correct — one handler method had outgrown itself."** Not a boundary
problem; a single-method-doing-two-things problem, fixed in place.

Everything else — diagnostics, allocation profile, replication fan-out, persistence keys, actor
runtime, event bus — **is still the right architecture for where Zenith is today.** This phase's
strongest result may be negative: twelve-plus phases of real growth, audited area by area with
actual evidence (repetition counts, line counts, measured allocation numbers), produced exactly
three small, mechanical, behavior-preserving improvements and zero new frameworks. That is what "the
architecture continues to be adequate" looks like when you actually go check, rather than assume it.

# Plan 002: A cause-aware policy seam for environment-driven world changes

## Status

- Priority: P2 (completes what Protect promises; nothing else depends on it yet)
- Category: architecture / correctness
- Effort: M across six independently reviewable phases
- Risk: MED. The danger is not the seam, it is the writers: a veto that leaves a writer retrying forever, drops an item while the block stays, or builds half a structure is worse than no veto.
- Depends on: plan 001 phases 0-3 (done)
- Status: DESIGN, revision 2 (after review). Nothing here is implemented. Phase 0 is next once the decisions at the end are confirmed.

Revision 2 changes, from review plus a second reading of the code: the cause list now follows the meaning of the change, not the system that makes it; the proposal is per cell; what happens after a protection is lifted is stated; gravity has explicit invariants (and the code has a pre-existing conservation hole there); tree growth is a planned multi-cell unit, which the per-cell rule alone would break; the ratchet is an IL call-site scan, not text search; policy faults get rate-limited logging; the `EnvironmentChanged` fact and region flags are deferred.

## Problem

Player edits (place, break, interact, bucket) pass `IBlockEditPolicy` before any effect. The world is also changed by systems that act without a player: creeper explosions, liquid flow, fire, falling blocks, support loss and random ticks. They call `World.TrySetBlock` directly, so a protected region (or any future policy) does not hold against them. A region that stops a player but not a creeper or a lava flow is not protection, and the gap is invisible to callers.

## Principle

The policy decides whether a change may happen. The system that owns the gameplay decides how to carry it out correctly (ordering, side effects, retries). The policy is never a central mutation mechanism, and the systems do not share an execution abstraction.

## Evidence: every non-player writer today, and its cause

Found by searching `TrySetBlock`/`SetBlock` in `Gameplay` and `Server` (player edit paths excluded), then reading each writer.

| Writer | Where | Change | Cause |
|--------|-------|--------|-------|
| Creeper explosion | `CreeperSystem.BreakBlocksInRadius` | sphere to air, no drops; per cell, `continue` on refusal | `Explosion` |
| Liquid flow | `FluidFlow.Place`/`Write` | writes liquid into open cells; calls `_beforeDestroy` (drops the washed-away block) **before** writing | `FluidFlow` |
| Fire spread / burn-out | `FireSpread.Write` | fire into cells, burns blocks away | `Fire` |
| Lava igniting | `LavaIgnitionBehavior` (via `tick.TrySet`) | fire next to flammable blocks | `Fire` |
| Falling blocks | `GravitySystem.BeginFall` / `Land` | clears the origin cell, later writes the landing cell | `Gravity` |
| Support loss | `BlockUpdateSystem.BreakUnsupported`, reshape | a neighbour edit invalidates a block; it breaks and drops, or reshapes | `SupportLoss` |
| Sapling growth | `SaplingGrowthBehavior` (via `tick.TrySet`) | age bit, then a whole tree (about 40 cells) | `Growth` |
| Leaves decay | `LeavesDecayBehavior` (via `tick.TrySet`) | leaves removed | `Decay` |
| Grass spread | `GrassSpreadBehavior` (via `tick.TrySet`) | dirt becomes grass | `Spread` |

Properties that shape the design:

1. Every writer already tolerates a refused write (`TrySetBlock` returns false, they `continue`). A per-cell veto fits their shape.
2. Two of them have side effects **before** the write (`FluidFlow` drops first; support loss drops after). A veto applied only at the write would lose or duplicate items.
3. Liquid and fire are re-driven by scheduled ticks. A veto that merely refuses the write makes them retry every tick.
4. Random-tick behaviours all write through `RandomTickContext.TrySet`, one choke point, but the cause belongs to the behaviour, not to the context.
5. **Tree growth is a planned multi-cell unit** (`Plan`, then write all). A per-cell veto on its writes would grow a trunk without a canopy, or the reverse.
6. **Gravity already has a conservation hole.** `Land` removes the entry and, if `TrySetBlock` fails, returns: "block is lost (rare, §95 non-goal)". And `BlockSpill.Drop`, the obvious fallback, does nothing when `DoTileDrops` is off or the block does not yield a drop, and ignores a failed deposit. So "turn it into an item" through `BlockSpill` is not conservative.

## Design

### A second, narrower policy type; not a flag on the player one

Player edits carry a `Principal` and a permission lookup. Environment changes have no actor. Making the principal optional would force every player-edit policy to handle a case that does not apply to it.

```csharp
enum EnvironmentCause { Explosion, FluidFlow, Fire, Gravity, SupportLoss, Growth, Decay, Spread }

/// One cell, one cause. The unit of decision is a cell; there is no batch form.
readonly record struct EnvironmentChangeProposal(EnvironmentCause Cause, ulong Tick, BlockCellChange Cell);

interface IEnvironmentChangePolicy { PolicyDecision Evaluate(in EnvironmentChangeProposal proposal); }

sealed class EnvironmentChangePolicies   // Register -> Freeze, first denial wins, a throw denies
```

Same rules as the other two policy sets: synchronous, game loop only, values only, fail closed, frozen after composition. Each cause maps to exactly one meaning, so a later "allow growth" can never release decay or spread by accident. `Protect` implements this interface next to its two others, over the same region store.

### Writers ask; no shared writer abstraction

Each writer receives the policy set through its constructor (as `BlockEditSystem` and `BucketSystem` now do) and asks per cell where it decides. Wiring: `ServerServices` gains the set, `ZenithServer` creates it and `ProtectModule.Compose` installs into it before `GameplayComposition.Register` freezes all policy sets, `WorldModule` hands it to the writers. To be re-verified against the live tree at the start of Phase 1.

### Granularity: per cell, except planned units

Player edits stay all-or-nothing. Environment changes are cell-wise: an explosion near a region leaves the region's blocks and carves the rest; this is stated in the policy's doc comment and tested.

Exception, stated as a rule: **a system whose unit of change is a planned multi-cell structure asks about every cell while planning and abandons the whole unit if any is vetoed.** Today that is tree growth only. The veto goes into `SaplingGrowthBehavior.TryAdd`, next to the existing "cell is taken" refusal, so a vetoed tree is simply a tree that does not grow this tick.

### Veto at the decision, not at the write

- **Fluid and fire:** the veto joins the "can this cell take liquid/fire" predicates, so a vetoed cell behaves like a wall: no write, no `_beforeDestroy` drop, nothing pending for it. This prevents both the retry loop and drop-without-removal.
- **Explosion:** per cell before the write; vetoed cells are skipped.
- **Support loss:** a vetoed dependent cell stays. This is intentional, not a side effect: the invariant is "a protected cell changes only with bypass", even if that leaves a torch on air. Tested, including what happens later (below).
- **Random ticks:** each behaviour passes its own cause; single-cell changes are vetoed in `TrySet`, tree growth in its planner.
- **Gravity:** see its own section.

### After a protection is lifted

Lifting a protection does **not** replay changes that were refused while it held. A refused change is gone, not queued. In practice: water, fire and falling blocks resume on the next neighbour update or scheduled tick that reaches them; sapling growth, decay and spread resume by themselves because random ticks keep coming; an unsupported block (the torch) stays until something updates its neighbourhood. This is accepted for v1 because the alternative is a retry queue per cause, which is the "god scheduler" this project rejects. It is documented here and pinned by tests, so it is a stated behaviour and not a surprise. If it proves unacceptable, the follow-up is a targeted re-evaluation (re-enqueue the cells of a removed region into the existing update queues), not a new mechanism.

### Gravity: invariants first

Gravity is split into its own phase because it has persistent state across ticks and a real conservation problem. The invariant, for every block that starts to fall: **at every tick boundary exactly one representation exists: the block at its origin, or one falling entry, or one dropped item stack, or (in the single documented failure case below) a logged loss. Never two, never silently none.**

- **At the start of a fall**, judge the origin (`block -> air`) and the predicted landing cell. If either is vetoed the block does not start to fall and stays where it is (same rule as support loss). This removes almost every landing veto before it can happen.
- **At landing**, if the cell is vetoed, or was claimed mid-fall, the entry converts to a drop through a dedicated conversion, not `BlockSpill.Drop`: it ignores `DoTileDrops` (a falling block is not terrain being broken; destroying it silently would be the loss) and it reports whether the deposit succeeded. The drop is created at the landing cell's position, the entry is removed in the same step, so the two representations never coexist.
- **If the deposit fails** (floor-drop cap), the entry stays alive and the conversion is retried on the following ticks, bounded (default 20 ticks). After the bound the loss is logged at error level with coordinates and block, counted in diagnostics, and the entry is removed. This is the one documented loss case; it replaces today's silent loss in `Land`.
- The same conversion also fixes the existing "cell claimed mid-fall" loss, which is a behaviour change to call out in that phase's commit.

Tests: veto at origin, veto at predicted landing, veto only at landing after the prediction (cell changed mid-fall), `DoTileDrops` off, deposit refused then retried, deposit refused past the bound (logged, counted, entry gone), and a count of block + entry + drop equal to one at every tick in each scenario. Needs a check that falling entries do not need persistence across restart (look at `FallingBlockStore` and the world save before starting).

### Observable faults

Fail-closed is kept, but a faulty policy must not freeze part of the world silently or flood the log. A small shared helper used by all three policy sets (the two existing ones are adopted in Phase 1): the first fault per policy and cause logs the policy type, cause or edit kind, cell and exception; repeats are counted and summarised at most once per N seconds. Tests: a throwing environment policy denies only the cells it was asked about and unrelated systems and causes keep working; 1000 faults produce a bounded number of log lines.

### Cost

Per-cell lookups run in fluid, fire and random ticks. Protect's lookup is a linear scan over at most 256 regions, with an empty-set early exit. No spatial index until measured. Phase 1 adds a repeatable measurement (diagnostics timings of the tick with and without regions, with a representative scenario each for an explosion, a water flow field, a fire field, and random ticks across a loaded area with several adjacent and overlapping regions). The index is added only if the measurement shows cost beyond noise, and the measurement is rerun in each later phase that adds a hot writer.

## Phases

Each phase is one reviewable commit with tests that assert the world and the items, not only a return value.

- **Phase 0, the ratchet.** An IL call-site scan in the style of `ArchitectureBoundaryTests` (it already walks IL; no new dependency, comments and dead text cannot trigger it, method pointers are seen). It finds every call to the cell-mutating members of the world and compares the calling types with an allowlist whose entries name their cause or "player edit, policed". Inventory first: before the test is written, list every member that can change a cell (not only `SetBlock`/`TrySetBlock`: also the column and section setters and any chunk replace path) so the allowlist covers all mutation paths, and say in the test's comment what the scan cannot see (reflection). Adding a writer without deciding its cause then fails a test.
- **Phase 1, seam + Explosion + fault log.** The types, composition, Protect implementation, `CreeperSystem` per cell, the shared fault-log helper adopted by the three sets, the first measurement. Tests: a crater stops at the boundary; protected blocks untouched, others carved; a faulty policy denies safely and logs boundedly; frozen after composition.
- **Phase 2, FluidFlow + Fire (incl. lava ignition).** Veto in the predicates. Tests: liquid and fire do not enter a region; no washed-away drop for a vetoed cell (item conservation); quiet afterwards (no re-queued tick for the vetoed cell); after the region is removed, resumption happens on the next neighbour update and not before (the documented semantics).
- **Phase 3, SupportLoss.** Tests: a torch inside a region survives its support being removed outside, with no drop; it remains on a later neighbour update while protected; once the region is removed it breaks on the next update and not before.
- **Phase 4, Gravity.** The invariants above, with their tests.
- **Phase 5, random ticks: Growth, Decay, Spread.** Per-behaviour cause; tree growth as a planned unit. Tests: no partial tree (a protected cell anywhere in the footprint means no trunk and no canopy); leaves inside a region do not decay; grass does not spread into a region; a region does not affect behaviours outside it.

## Deferred, with the trigger that reopens each

- **`EnvironmentChanged` fact** (batched, per system per tick). Reopen when a concrete consumer exists (an audit log, rollback, an integration). Policies solve the current problem; facts serve needs not yet shown.
- **Per-region allowed-cause flags** and `/protect allow`. v1 is strict: a region resists every cause. Known consequence, accepted: inside a region saplings do not grow, leaves do not decay and grass does not spread. Reopen at the first real need to let one of them through; the codec version already exists to carry the change.
- **Chunk index for regions.** Only on measured cost.
- **Re-evaluation when protection is lifted.** Only if the documented semantics prove unacceptable.

## Decisions to confirm

1. Strict regions in v1 (consequence above accepted).
2. Support loss inside a region is vetoed and the block stays, as intentional behaviour.
3. Gravity's retry bound (20 ticks) and the single documented logged-loss case.
4. Phase 0 first, alone, as the next commit.

## Non-goals

A public environment-event API, per-block-type rules, a rollback log, a scheduler change, explosion physics (blast resistance, drops, ray casting), a shared world-writer abstraction, and anything outside `Gameplay`'s existing writers.

## STOP conditions

- A writer cannot be vetoed without leaving a half-applied effect (a side effect before the decision that cannot be reordered): stop and report that writer; do not veto after the fact.
- The per-cell lookup shows up in tick diagnostics beyond noise: stop and add the chunk index before continuing.
- Gravity needs state the falling-block store does not carry through a veto or a restart: record it, do not work around it.
- The Phase 0 inventory finds a mutation path that the allowlist approach cannot attribute to a cause: stop and decide it before the seam is built.

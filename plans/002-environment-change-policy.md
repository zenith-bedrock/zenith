# Plan 002: A cause-aware policy seam for environment-driven world changes

## Status

- Priority: P2 (completes what Protect promises; nothing else depends on it yet)
- Category: architecture / correctness
- Effort: M across five independently reviewable phases
- Risk: MED. The danger is not the seam, it is the writers: a veto that leaves a writer retrying forever, or that drops an item while the block stays, is worse than no veto.
- Depends on: plan 001 phases 0-3 (done)
- Status: DESIGN ONLY. Nothing here is implemented. Phase 0 is the first step; it needs the owner's go-ahead on the decisions in "Decisions to confirm".

## Problem

Player edits (place, break, interact, bucket) pass `IBlockEditPolicy` before any effect. The world is also changed by systems that act without a player: creeper explosions, liquid flow, fire, falling blocks, support loss and random ticks. They call `World.TrySetBlock` directly, so a protected region (or any future policy) does not hold against them. A region that stops a player but not a creeper or a lava flow is not protection, and the gap is invisible to callers.

## Evidence: every non-player writer today

Found by searching `TrySetBlock`/`SetBlock` in `Gameplay` and `Server` (player edit paths excluded):

| Writer | Where | What it does | Shape |
|--------|-------|--------------|-------|
| Creeper explosion | `CreeperSystem.BreakBlocksInRadius` | carves a sphere to air, no drops | loop, per cell, `continue` on failure |
| Liquid flow | `FluidFlow.Place`/`Write` | writes liquid into open cells; calls `_beforeDestroy` (drops what it washes away) **before** writing | per cell, driven by scheduled ticks |
| Fire | `FireSpread.Write` | spreads fire, burns blocks away | per cell, scheduled ticks |
| Falling blocks | `GravitySystem` (two writes) | clears the origin cell, later writes the landing cell | a pair across time |
| Support loss | `BlockUpdateSystem.BreakUnsupported` and reshape | a neighbour edit makes a block invalid; it breaks and drops, or reshapes in place | consequence of an edit |
| Random ticks | `RandomTickBehavior` (leaves decay, sapling growth, spreading) | one cell per behaviour | per cell |

Observed properties that shape the design:

1. Every one of them already tolerates a refused write (`TrySetBlock` returns false, they `continue`). A per-cell veto fits their shape; an all-or-nothing batch does not.
2. Two of them have side effects **before** the write (`FluidFlow` drops the washed-away block first; support loss drops after). A veto applied only at the write would lose or duplicate items.
3. Liquid and fire are re-driven by scheduled ticks. A veto that merely refuses the write makes them retry every tick.

## Design

### A second, narrower policy type; not a flag on the player one

Player edits carry a `Principal` and a permission lookup. Environment changes have no actor. Overloading `BlockEditProposal` with "maybe a principal" would make `PermissionBlockEditPolicy` and every future policy handle a case that does not apply to it. So:

```csharp
enum EnvironmentCause { Explosion, FluidFlow, Fire, Gravity, SupportLoss, Growth }

readonly ref struct EnvironmentChangeProposal(EnvironmentCause cause, ulong tick, ReadOnlySpan<BlockCellChange> cells)

interface IEnvironmentChangePolicy { PolicyDecision Evaluate(in EnvironmentChangeProposal proposal); }

sealed class EnvironmentChangePolicies   // register -> Freeze, first denial wins, throw => deny, same as the other two sets
{
    public bool Permits(EnvironmentCause cause, in BlockCellChange cell);   // one-cell convenience for the hot path
}
```

Same rules as the other policy sets: synchronous, game loop only, values only, fail closed, frozen after composition. `Protect` implements this interface next to the two it already has, using the same region store, so one region definition governs every cause.

### Writers ask; they do not get a new scheduler or base class

Each writer receives the policy set through its constructor (like `BlockEditSystem` and `BucketSystem` now) and asks per cell at the point it decides. No shared "world writer" abstraction: the writers differ too much, and ADR 97 forbids creating one without two real uses that agree.

### Per-cell, not per-batch

Player edits stay all-or-nothing (a door is one edit). Environment batches are cell-wise by contract: an explosion near a region leaves the region's blocks and carves the rest. This is stated in the policy's doc comment and tested, so nobody later expects atomic craters.

### Veto at the decision, not at the write

- **Fluid and fire:** the veto joins the "can this cell take liquid/fire" predicates (`CanSpreadTo`/`CanHold`-style), so a vetoed cell behaves like a wall: no write, no `_beforeDestroy` drop, and no retry because nothing is pending for it. This is what prevents the retry loop and the drop-without-removal bug.
- **Explosion:** per cell before the write; vetoed cells are skipped.
- **Support loss:** if the dependent cell is vetoed it stays (unsupported but present). It is re-evaluated only on a later neighbour update, which is what already triggers it.
- **Gravity (last):** judge the origin when the block would start to fall and the landing when it would land. A veto at landing converts the block to its item drop (conservation), never deletes it.
- **Growth:** judged like the rest, but see the region flags below.

### Facts

An applied environment change is not published per cell (fluid can change thousands). One batched `EnvironmentChanged(Cause, Tick, Cells)` fact per system per tick, only when non-empty. This is the last phase and is optional until a subscriber needs it. Policies come first because they are what protection needs; facts are what logging and rollback need.

### Region flags (data shape only for now)

"Protect everything from everything" breaks spawn trees (growth) and is also too blunt for fire. The region needs a set of causes it allows. v1 of this plan keeps the strict default (a region resists every cause) and reserves a `Allowed` bit set in the codec (version 2, absent means none allowed) and a later `/protect allow <name> <cause>`. Strict by default is the safe direction: a missing allowance is noticed, a missing protection is not.

## Phases

Each phase is one reviewable commit with tests that assert the world and the items, not only a return value.

- **Phase 0, the ratchet.** A test that scans `Gameplay` for `TrySetBlock(`/`SetBlock(` and compares the files against an allowlist, each entry naming its cause or "player edit, policed". Adding a new writer without deciding its cause then fails a test. This is cheap, needs no seam, and makes the gap list self-maintaining. It also fixes the current list in code instead of prose.
- **Phase 1, seam + Explosion.** The policy types, composition (frozen with the others), Protect implementation, `CreeperSystem` per-cell. Tests: a crater stops at a region boundary, blocks inside are untouched, blocks outside are carved, a throwing policy leaves everything standing, the set cannot be extended after freeze.
- **Phase 2, FluidFlow + Fire.** Veto in the spread predicates. Tests: water and lava do not enter a region, no washed-away drop appears for a vetoed cell (item conservation), and the system is quiet afterwards (no re-queued tick for the vetoed cell).
- **Phase 3, SupportLoss.** Tests: a torch inside a region survives the removal of its support outside, with no drop.
- **Phase 4, Gravity + Growth + region flags.** Tests: a falling block that cannot land becomes its drop; a region that allows `Growth` lets a sapling grow while a region that does not, blocks it.
- **Phase 5, `EnvironmentChanged` facts.** Batched, immutable payload, test that a vetoed change publishes nothing.

## Risks

- **Hot path cost.** Fluid, fire and random ticks ask per cell per tick. Protect's lookup is a linear scan over at most 256 regions. Mitigation order: the empty-set early exit (already there), then a per-chunk index of regions, added only if the diagnostics tick timings show a cost. Do not add the index speculatively.
- **Livelock.** Covered by vetoing in predicates; each phase must test "quiet after veto".
- **Item conservation** at fluid wash-away and gravity landing; tested explicitly.
- **Partial-effect surprise.** Per-cell is deliberate; the doc comment and a test pin it.

## Decisions to confirm

1. Strict default (a region resists every cause) with allowances added later. Recommended.
2. Support loss inside a region is vetoed (the dependent block stays), rather than allowed. Recommended, because the invariant "a protected cell changes only with bypass" is then simple and testable.
3. Phase 0 first, alone, as the next commit. Recommended.

## Non-goals

A public environment-event API, per-block-type rules, a rollback log, a scheduler change, explosion physics (blast resistance, drops, ray casting), and anything outside `Gameplay`'s existing writers.

## STOP conditions

- A writer cannot be vetoed without leaving a half-applied effect (side effect before decision that cannot be reordered): stop and report that writer; do not veto after the fact.
- The per-cell lookup shows up in tick diagnostics beyond noise: stop and add the chunk index before continuing.
- Gravity needs state the falling-block store does not carry to survive a veto: record it, do not work around it.

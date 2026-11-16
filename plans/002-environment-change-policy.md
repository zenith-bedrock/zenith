# Plan 002: A cause-aware policy seam for environment-driven world changes

## Status

- Priority: P2 (completes what Protect promises; nothing else depends on it yet)
- Category: architecture / correctness
- Effort: M across six independently reviewable phases
- Risk: MED. The danger is not the seam, it is the writers: a veto that leaves a writer retrying forever, drops an item while the block stays, or builds half a structure is worse than no veto.
- Depends on: plan 001 phases 0-3 (done)
- Status: DESIGN, revision 4. Phase 0 (the ratchet) is approved and done. Phase 4a (falling-block persistence) is approved and closed. Phase 1 (the seam, Explosion, the shared fault log) is implemented. You delegated the order of the rest ("choose the best approach"): the order stays 2, 3, 4b, 5, each its own commit. Decision 3(b), the void as the single named exit, is approved.

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

### Gravity: conservation as a real invariant

Rejected from revision 2: dropping a falling block after a bounded retry. A veto, a full drop store or a retry limit must never delete a block. The invariant is now:

> For every block that starts to fall, at every tick boundary exactly one representation exists: the block in the world at its origin, or one live falling entry, or the block in the world at its resting place. It leaves that set only by landing as a block, or by the single named vanilla exit (the void, below). Failure, a veto, a full store, a retry limit or a restart never remove it.

**What the code does today** (investigated; none of this is changed yet):

1. `FallingBlockStore` is memory only. Nothing saves it. A graceful shutdown force-lands every entry before the final flush (`ZenithServer`, `SettleAllPending`), so a clean stop conserves blocks. A crash, a kill, or an exception thrown by `SettleAllPending` (it is caught and logged as a warning) loses every in-flight block: `BeginFall` already cleared the origin, and the origin clear reaches disk within the write-behind drain (about 50 ms). **Pre-existing crash-durability gap.**
2. `FloorDropStore` cannot carry the guarantee. It is memory only too (not in any save path), refuses new cells at `SoftCap` 2048, searches a radius of 3 for a free cell, and despawns a drop after 6000 ticks. `BlockSpill.Drop`, the helper a fallback would reach for, additionally does nothing when `DoTileDrops` is off or the block yields no drop, and discards the deposit result. So "convert to a drop" is not a conserving step: a deposit can fail, an accepted deposit is not durable, and the store has no way to reserve room. (The store does offer `CanDeposit`, used by player death to check room before removing a source; that is the right pattern for what it covers, but it does not fix persistence or despawn, and a dropped block is an ordinary item with ordinary item rules.)
3. `Land` only fails to write when the cell is outside the world's height (`TrySetBlock`'s only refusal), so its "block is lost" comment describes a case that cannot happen for an in-range landing. But `Land` writes with `TrySetBlock` and never checks that the landing cell is replaceable: a block placed into the resting cell in the same tick would be overwritten, which destroys **that** block. Needs a test before the phase starts; if confirmed it is a pre-existing bug to fix with the phase.
4. `TrySpawn` past `SoftCap` 512 makes `BeginFall` resolve the fall instantly at the current support. That conserves today, but it is the path that would have nowhere to put a block whose landing is vetoed, because by then the origin is already cleared.
5. Falls into the void (no support down to the world floor, or the entry's Y below it) remove the entry without placing anything. This matches vanilla, where a falling block outside the world is discarded.
6. A column can unload while an entry above it falls (residency counts viewers and floor drops, not falling entries). Landing then loads the column synchronously. Not a loss; noted for the cost review.

**Proposal: the block is its own reservation.** Do not convert to an item at all. A vetoed landing moves the block, it does not destroy or transform it.

- **Decide before clearing the origin.** `BeginFall` first checks, in this order and with nothing written: the origin is permitted to be cleared, the predicted landing cell (the first blocking support below, found without moving anything) is permitted to receive the block, and the store has room for an entry. If any check fails the block stays at its origin this tick, exactly like support loss under protection, and is re-enqueued with the existing deferral so it is judged again later. This removes the store-full case and almost every veto case before any state changes, and it replaces today's "clear, then spawn, then resolve instantly" order.
- **Mid-fall changes** (a region created while falling, a cell filled below) are handled at the landing step: the block rests at the lowest cell of its fall column that is free **and** permitted, searching upward from the predicted landing toward its origin. A protected cell therefore behaves like ground for the falling block. Placing it is judged as `air -> block` under cause `Gravity`.
- **No permitted resting cell** (the origin itself was permitted to clear but not to receive, and everything between is protected: a pathological policy): the entry stays alive at its last position, marked stuck. It is logged once at error level with block, coordinates and cause, counted in diagnostics, and re-evaluated every 20 ticks. The 20-tick figure only paces re-evaluation and the log; it never ends the entry. The block exists as an entry until a cell accepts it.
- **Shutdown** uses the same resting rule, immediately and without animation. A stuck entry at shutdown is the case that cannot be made to rest; see persistence below.
- **Which cells accept a falling block** is settled by phase 4a, see "Phase 4a result": air, fire, any liquid, ground-cover plants and torches. Nothing else is ever overwritten; the block rests above it.
- **The void** stays the one named exit, as in vanilla. It is not caused by policy, capacity or failure, and it is deterministic. **Needs your confirmation** (decision 3 below): the alternative is to forbid starting a fall that has no support above the world floor, which keeps the block but differs from vanilla and only matters in worlds without a bedrock floor.

**Persistence and restart (the limit you asked about).** Implemented as phase 4a, see "Phase 4a result". The first sketch of this section (a separate world record written before the origin clear, with a three-way recovery rule on the origin cell) was replaced: a separate record can disagree with the column image in both directions, and the recovery rule "origin holds the same block, so discard the entry" destroys a block when a second sand block was placed in the vacated cell. The saved record now lives in the same image as the cells.

**Tests (4b):** veto at origin keeps the block and does not leave a stuck entry; veto at predicted landing keeps the block at origin; store full keeps the block at origin; region created mid-fall makes the block rest above it; no permitted cell keeps a live stuck entry that is never removed and is logged once; landing cell filled meanwhile does not destroy the new block; `DoTileDrops` off changes nothing; shutdown settle under a region; and in every scenario a count of blocks plus entries equals the number that started, at every tick. The 4a tests are listed in "Phase 4a result".

### Observable faults

Fail-closed is kept, but a faulty policy must not freeze part of the world silently or flood the log. A small shared helper used by all three policy sets (the two existing ones are adopted in Phase 1): the first fault per policy and cause logs the policy type, cause or edit kind, cell and exception; repeats are counted and summarised at most once per N seconds. Tests: a throwing environment policy denies only the cells it was asked about and unrelated systems and causes keep working; 1000 faults produce a bounded number of log lines.

### Cost

Per-cell lookups run in fluid, fire and random ticks. Protect's lookup is a linear scan over at most 256 regions, with an empty-set early exit. No spatial index until measured. Phase 1 adds a repeatable measurement (diagnostics timings of the tick with and without regions, with a representative scenario each for an explosion, a water flow field, a fire field, and random ticks across a loaded area with several adjacent and overlapping regions). The index is added only if the measurement shows cost beyond noise, and the measurement is rerun in each later phase that adds a hot writer.

## Phases

Each phase is one reviewable commit with tests that assert the world and the items, not only a return value.

- **Phase 0, the ratchet. DONE, see "Phase 0 result".**
- **Phase 1, seam + Explosion + fault log. DONE, see "Phase 1 result".** The types, composition, Protect implementation, `CreeperSystem` per cell, the shared fault-log helper adopted by the three sets, the first measurement. Tests: a crater stops at the boundary; protected blocks untouched, others carved; a faulty policy denies safely and logs boundedly; frozen after composition.
- **Phase 2, FluidFlow + Fire (incl. lava ignition).** Veto in the predicates. Tests: liquid and fire do not enter a region; no washed-away drop for a vetoed cell (item conservation); quiet afterwards (no re-queued tick for the vetoed cell); after the region is removed, resumption happens on the next neighbour update and not before (the documented semantics).
- **Phase 3, SupportLoss.** Tests: a torch inside a region survives its support being removed outside, with no drop; it remains on a later neighbour update while protected; once the region is removed it breaks on the next update and not before.
- **Phase 4a, falling-block durability. DONE, awaiting review; see "Phase 4a result".**
- **Phase 4b, gravity under policy.** The invariant, the decide-before-clearing order, the resting rule and the stuck-entry rule above, with their tests. Not started.
- **Phase 5, random ticks: Growth, Decay, Spread.** Per-behaviour cause; tree growth as a planned unit. Tests: no partial tree (a protected cell anywhere in the footprint means no trunk and no canopy); leaves inside a region do not decay; grass does not spread into a region; a region does not affect behaviours outside it.

## Phase 1 result

- **Seam.** `EnvironmentCause`, `EnvironmentChangeProposal(Cause, Tick, Cell)`, `IEnvironmentChangePolicy` and `EnvironmentChangePolicies` (register, freeze, first denial wins, a throw denies) in `Gameplay/Policy/EnvironmentChangePolicy.cs`. `Allows(cause, tick, x, y, z, before, after)` is the call writers use. Composition mirrors the other two sets: `ServerServices` and `ServerContext` carry the set, `ZenithServer` creates it, `ProtectModule.Compose/Install` registers `ProtectEnvironmentPolicy`, `GameplayComposition.Register` freezes it.
- **Protect.** `ProtectEnvironmentPolicy` vetoes every cause inside a region and none outside; no actor, so no bypass. Strict, as decided.
- **Fault log, shared.** `PolicyFaultLog` replaced the one-line error each set wrote per failure: the first three failures of a policy type in full, then one line per thousand, with the running count. The block-edit and container sets use it too. Reason: environment policies run per cell per tick, and a throwing policy would otherwise write thousands of lines a second.
- **First writer.** `CreeperSystem.BreakBlocksInRadius` asks per cell under `Explosion`; a vetoed cell is left exactly as it is and the rest of the crater is carved. `WorldWriterInventoryTests` has a new kind, `EnvironmentPoliced`, and pins that five environment writers remain unpoliced (was six).
- **Tests (`EnvironmentPolicyTests`).** First denial wins, empty set allows; a throwing policy denies and logs 5 lines for 2500 failures; the block-edit set logs 4 for 1500; frozen after composition; Protect vetoes all eight causes inside and none outside; without a region the crater is the full sphere; a crater straddling a region carves exactly the cells outside it and keeps exactly the cells inside (both counts non-zero, so the test cannot pass vacuously); a throwing policy leaves every cell. Removing the policy call from the creeper fails two of them.
- **Measurement (first, and as a number, not an adjective).** Cost of `Allows` on the frozen set, per cell, release-like loop on this machine: 61 ns with no regions, 105 ns with 1, 312 ns with 16, 3.5 microseconds with 256 (the cap). `ProtectRegions.TryFind` is a linear scan, so cost grows with the region count. A creeper crater is at most about 120 cells, so the worst case is about 0.4 ms once; fluid flow and fire write fewer cells per tick but every tick. This is below the stop condition ("beyond noise in tick diagnostics"), so no chunk index yet; the number to watch is cells asked per tick times region count. Re-measure in Phase 2 with real flow.

## Phase 0 result

**Inventory of members that change a world cell** (read from `World`, `WorldColumn`, `Section`): `World.TrySetBlock` (two overloads), `World.SetBlock`, `WorldColumn.Set`, `WorldColumn.SetSection`, `Section.Set`. `Section.Uniform` and `Section.FromIds` only build sections, which reach the world through `SetSection`. Block entities (`World.SetBlockEntity`, `RemoveBlockEntity`, `WorldColumn.SetEntity`, `RemoveEntity`) change container contents, not cell ids; they are outside this ratchet and covered by the container-access policy and the inventory conservation tests.

**Every production caller found by the IL scan** (type.method, kind, cause):

| Caller | Kind | Cause / note |
|--------|------|--------------|
| `BlockEditSystem.TryPlace`, `TryBreak`, `CommitExtraWrites` | player edit, policed | place, break, coupled cells and door use; `BlockEditPolicyTests`, `ProtectModuleTests`, `ProtectInteractionTests` |
| `BucketSystem.TryScoop`, `TryPour` | player edit, policed | judged as break / place; `ProtectInteractionTests` |
| `CreeperSystem.BreakBlocksInRadius` | environment | `Explosion` |
| `FluidFlow.Write` | environment | `FluidFlow` |
| `FireSpread.Write` | environment | `Fire` |
| `GravitySystem.BeginFall`, `Land` | environment | `Gravity` (origin, landing) |
| `BlockUpdateSystem.BreakUnsupported`, `Evaluate` | environment | `SupportLoss` (break and drop; neighbour-driven reshape) |
| `RandomTickContext.TrySet` | environment | per behaviour, below |
| `World.SetBlock`, `World.TrySetBlock`, `World.BuildColumn`, `ColumnCodec.Decode`, `WorldColumn.Set` | world internals | the write API, generation, loading, column storage |

Random-tick behaviours that write through `RandomTickContext.TrySet`: `SaplingGrowthBehavior` (`Growth`, a whole tree is one planned unit), `LeavesDecayBehavior` (`Decay`), `GrassSpreadBehavior` (`Spread`), `LavaIgnitionBehavior` (`Fire`).

No caller exists outside this table: nothing in `Server`, `Session`, `Protocol` or `Packets` changes a cell, and `SetBlock` has no production caller except through `TrySetBlock`. `WorldWriterInventoryTests` pins this at method granularity, fails for an unlisted writer, fails for a stale entry, pins that exactly six environment writers remain unpoliced, and requires every random-tick writer to name its cause. It was checked to fail when an entry is removed. What it cannot see is reflection or dynamic invocation, and it proves only that no unlisted code writes a cell; that a listed "policed" writer really consults the policies is shown by the behavioural tests named in the table.

## Phase 4a result

Scope: conservation of falling blocks across crashes and mid-fall changes. No policy is consulted yet; that is 4b.

### Vanilla landing semantics, and the rule adopted

Researched on the Minecraft Wiki's Falling Block page (read for this phase, not recalled). Vanilla: a falling block becomes a block again only when the cell it reaches is replaceable (air, liquids, fire, and replaceable ground cover such as grass, ferns and snow layers) and the block below supports it; otherwise it breaks into an item (Bedrock since 0.8.0 build 8). A falling block that lands in a non-replaceable cell does not overwrite it. Bedrock adds snow layers to a layer of snow. What the page does not say, and I could not verify without a client: what happens to a fall that leaves the world (the void), to a cell filled during the fall, or with game rules that disable drops.

Adopted rule (`GravitySystem.Accepts`): a falling block may occupy and land in **air, fire, any liquid, ground-cover plants and torches** (`Blocks.IsFluid` or `FluidFlow.CanHold`: the set a liquid washes away in Zenith). **Any other cell is never overwritten**, including every block that blocks movement and open doors, trapdoors and gates; the fall stops above it.

Divergences from vanilla, deliberate and recorded:

1. Vanilla turns the block into an item when the cell is not replaceable. Zenith rests it on top instead. Reason: an item is not a conserving step here (floor drops are memory only, capped, despawn, and `DoTileDrops` can suppress them), and the invariant is conservation. This is the one place Zenith keeps more than vanilla.
2. Vanilla replaces some thin blocks Zenith does not model as replaceable (snow layers) and keeps some Zenith treats as replaceable (torches are destroyed by landing sand in Zenith; in vanilla Java they are broken too, in Bedrock I did not verify this). A torch or plant **placed during the fall in the landing cell is crushed**, because the falling block already passes through those cells; accepting that keeps the fall and the landing consistent. Everything that blocks movement placed during the fall is kept.
3. Open doors, trapdoors and gates used to be fall-through cells (and were overwritten on landing). They now stop the fall. This changes behaviour that was an overwrite bug, not a feature.
4. The void is unchanged and is the single named exit (decision 3(b), approved): a block that falls below the world's floor is discarded and its record cleared. It is not a policy outcome, a capacity outcome or a failure, and no other state discards a block.

**What "void" means here, kept narrow.** Three different conditions are not to be confused. (a) *No support below*: the cell under the block accepts it; the block keeps falling and is still conserved. (b) *A replaceable cell*: a cell that accepts the block; it is a place to land, never an exit. (c) *The lower world limit*: the block has fallen to below `FlatMinY` through accepting cells all the way down. Only (c) is the void. It is decided by the world's height and the accepting rule alone, with no special state and no preventive refusal to start a fall.

I did not restrict falling to simplify: the only behaviours that became stricter are the ones that destroyed a block that blocks movement or an open door.

### Design

- **The record is part of the column image.** Gravity is vertical, so the cell a block leaves and the cell it lands in are in the same column. `WorldColumn` carries a list of `PendingFall` (local x, z, origin Y, block); `ColumnCodec` version 2 saves it after the block entities (version 1 images still load, with no falls). Because the cells and the record are encoded together by the single writer of column images (`World.PersistColumn`), no saved image can show the cleared origin without its record, or the landed block together with its record.
- **A refinement of the wording of requirement 1 and 6, stated so you can reject it.** You asked for the record to be durably written before the origin is cleared, and removed only after the landing is durably confirmed. A separate record needs that ordering and a durability barrier between the two writes. Putting both in one image gives the same guarantee by construction (every image is either before the clear or after it, either before the landing or after it) and needs no barrier. The cost is that **the record is not durable at the instant a fall starts or ends.** It is written when the column is next saved (the residency sweep, eviction or shutdown), as part of the same image as the cells, through the store's normal write-behind schedule. The guarantee is atomicity of the image, not immediacy: a crash before that save leaves the previous image, which holds the block at its origin with no record, still exactly one block. Nothing in this plan or the code should be read as "durable when the fall begins or lands"; there is no fsync per fall.
- **`BeginFall`**: record the fall (into the column), then clear the origin, in the same tick on the game loop. `Land`: write the block at the first cell from the landing cell upward that accepts it, then end the fall (entry leaves the store, record leaves the column) in the same step. `Resolve` is the only place that ends a fall; its callers are a landing and the void.
- **Recovery**: a column loaded from storage that carries records announces itself once (only the copy that wins the publish race does). Gravity adopts each record once, creating a falling entry at its origin height; the normal fall then lands it, and the destination is judged at landing time, not at record time. The origin cell is not special: if something solid now sits there the block rests above it; if the same block sits there it is a different block (a player may have put sand back) and the record is kept. Blocks stacked above the origin are queued again, since the queue that held them was memory only.
- **A column with a block in the air is not evicted** (`TryEvictChunk`), so the entry and the column that holds its record live and die together and a landing never reloads the column mid-fall. This also removes limit 6 above.
- **A block no cell accepts** (the column is full to the world's top) stays an entry, marked stuck, is logged once at error level, is saved with its record, and is retried every 20 ticks. The 20 only paces the retry; it never ends the entry.
- **Shutdown** (`SettleAllPending`) now adopts any loaded column's records first, so a stop right after boot also settles them.

### Limits, stated honestly

- A fall whose column nobody has loaded since the crash waits in storage with its record and falls when the column is next needed. The block is conserved but not visible in the world until then.
- Durability is the column save schedule (see above), not an fsync per fall. The loss window is "the fall since the last save", which resolves to the earlier consistent state, never to a lost or duplicated block.
- Nothing here was exercised against a real Bedrock client; the tests drive the systems directly.
- A stuck entry holds its column resident for as long as it is stuck. It needs a column filled to the world's top, or (4b) a policy that refuses every cell.

### Tests (`FallingBlockDurabilityTests`)

The crash is a second world booted from `InMemoryChunkStorage.Snapshot()`: the bytes written so far, none that were only in memory.

- A crash at every tick boundary of a whole fall, both before and after the tick was saved, resolves to exactly one block and no record; the live count (world plus entries) is one at every boundary; what is saved after recovering holds no record.
- No saved image shows the cleared origin without its record, or a landed block with one, at every tick.
- Two stacked blocks in flight both survive.
- Recovery adopts once however often it is asked; booting twice from the same image gives the same world; a column nobody loaded keeps its record in storage until loaded.
- A block placed in the landing cell or in the origin cell before recovery, and a block placed mid-fall in the landing cell, is never overwritten; the fall rests above it and the count is one.
- Destination types: water, fire, short grass and torch give way; stone and an open door stay and the block rests above.
- A column full to the top keeps a stuck, saved, once-logged entry through a restart, and it lands within a retry period once room appears.
- The void clears the record with the entry. A column with a block in the air is not evicted until it lands. A graceful stop, including one right after boot, leaves no record.
- Version 1 images still load; a record outside its column is rejected as corrupt.
- Checked to fail: removing the record clear from `Resolve` fails nine of them.
- The crash boundaries covered, each by a snapshot of storage and a second world: before the first tick (block at origin), after the tick that recorded the fall and cleared the origin, at every tick in flight, in the tick that lands (saved and not yet saved), after the landing is saved, a stuck entry, the void, and graceful stop (also right after boot).

### The intermittent random-tick failure

The full-suite run that first showed `RandomTickSystemTests.Grass_does_not_spread_to_dirt_with_a_block_on_top` failing once was investigated, not dismissed. It is not related to gravity: the same intermittent failure (here `A_sapling_grows_into_a_tree_after_two_stages`) reproduces on the commit before 4a (3 of 15 runs). Cause shown by experiment: several test classes call `Blocks.Load(...)` in their setup, replacing the process-wide block tables while other tests run; a probe test that runs the sapling scenario while another thread reloads `Blocks` fails (1 of 15 rounds), and the same probe without the reload passed 3 of 3 runs. The setup-only reloads in `ChestFacingTests`, `CraftingDataPacketTests`, `ItemPaletteTests` and `TerrainProviderTests` now use `Blocks.EnsureLoaded()`; a run of the affected classes together went from 9 failures in 15 to 0 in 15. `BlockPaletteTests` still reloads on purpose because it tests `Blocks.Load`, so the race is reduced, not removed. The exact way a mid-test swap of the tables breaks the sapling scenario was not traced to a line; the experiment fixes the cause at the level of the shared reload, not the line.

## Deferred, with the trigger that reopens each

- **`EnvironmentChanged` fact** (batched, per system per tick). Reopen when a concrete consumer exists (an audit log, rollback, an integration). Policies solve the current problem; facts serve needs not yet shown.
- **Per-region allowed-cause flags** and `/protect allow`. v1 is strict: a region resists every cause. Known consequence, accepted: inside a region saplings do not grow, leaves do not decay and grass does not spread. Reopen at the first real need to let one of them through; the codec version already exists to carry the change.
- **Chunk index for regions.** Only on measured cost.
- **Re-evaluation when protection is lifted.** Only if the documented semantics prove unacceptable.

## Decisions

1. Strict regions in v1: **approved**. Saplings do not grow, leaves do not decay and grass does not spread inside a region.
2. Support loss inside a region is vetoed and the block stays, even if that leaves a state vanilla would not allow: **approved**; tests for behaviour after protection is lifted stay.
3. Gravity: **rejected as written**. Replaced by the conservation invariant, the block-as-reservation proposal and Phase 4a/4b above. (a) Phase 4a, persist the in-flight set: **approved and done**, with the refinement in "Phase 4a result". (b) The void as the single named vanilla exit: **approved**, with the narrow meaning in "Phase 4a result".
4. Phase 0 first, alone: **approved and done**. Phase 1 does not start until Phase 0 is reviewed.

## Non-goals

A public environment-event API, per-block-type rules, a rollback log, a scheduler change, explosion physics (blast resistance, drops, ray casting), a shared world-writer abstraction, and anything outside `Gameplay`'s existing writers.

## STOP conditions

- A writer cannot be vetoed without leaving a half-applied effect (a side effect before the decision that cannot be reordered): stop and report that writer; do not veto after the fact.
- The per-cell lookup shows up in tick diagnostics beyond noise: stop and add the chunk index before continuing.
- Gravity cannot keep exactly one representation of a block in some scenario: stop and report it; never resolve it by removing the entry.
- The Phase 0 inventory finds a mutation path that the allowlist approach cannot attribute to a cause: stop and decide it before the seam is built.

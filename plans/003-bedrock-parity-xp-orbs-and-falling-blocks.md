# 003: Bedrock parity review, XP orbs and falling blocks

- Status: REVIEW done. Phase B (scheduled start of a fall) is implemented; phase A (XP orbs) follows. Each its own commit, in the order below.
- Sources read: Zenith (`MobKillReward`, `DamageableActorCombatCore`, `PlayerExperience`, `FloorDropSystem`, `GravitySystem`, `GravityPendingStore`, `BlockEditSystem.NotifyGravityAfterEdit`), PocketMine (`ExperienceOrb.php`, `FallableTrait.php`), Dragonfly (`experience_orb*.go`, `falling_block*.go`), the Mojang protocol docs (`SpawnExperienceOrbPacket`). What is vanilla is marked with how sure it is; a number marked "to verify" should be checked against BDS with the differential tool before it is coded as a constant.

## 1. Experience: what Zenith does now, and the gap

Zenith credits the kill's experience straight to the killer: `DamageableActorCombatCore` calls `MobKillReward.AwardExperience`, which does `player.AddExperience(amount)` and sends the attributes in the same tick. There is no entity. The level maths (`PlayerExperience`) is right (vanilla thresholds, multi-level carry). Mobs are the only source of experience.

Vanilla (all three references agree on the shape): death spawns `minecraft:xp_orb` entities at the mob, popping out with a small random velocity. Orbs fall with gravity 0.04 and drag 0.02, home in on the nearest player within 8 blocks (checked about once a second), are pulled with a force that grows as they get close, and are collected when their box touches the player's, which plays the orb pickup sound and credits the points. They despawn after 5 minutes (6000 ticks). A total is split into orbs of fixed sizes, largest first: 2477, 1237, 617, 307, 149, 73, 37, 17, 7, 3, 1. The orb's value travels in entity metadata (`EXPERIENCE_VALUE`), and the client picks the sprite size from it.

What is missing, in order of how visible it is: the orb entity itself (spawn, motion, homing, pickup, sound, despawn), the split sizes, merging of nearby orbs (vanilla merges orbs of the same value that touch; not in PocketMine or Dragonfly, so it is optional and last), experience from sources other than mobs (ore mining, smelting, breeding, fishing: each is its own later change, listed in section 4), and persistence of orbs across restart (vanilla saves them; ours would not, the same as floor drops and falling blocks, and for the same stated reason).

### Proposal, phase A: experience orbs

- **Shape.** A fourth RAM-only entity store next to floor drops, falling blocks and projectiles, modelled on `FloorDropStore`/`FloorDropSystem` (the closest existing thing: pose, motion step, pickup, despawn, replication to peers that know the chunk). Not a framework, not the ECS: ADR §97 and §32 apply, one concrete type with one owning system. `ExperienceOrbStore` (cap, like the others, with refuse-and-warn-once) and `ExperienceOrbSystem`.
- **Gameplay decides, the protocol transmits.** The system decides spawn, motion, target and collection. `Protocol` gets `SendAddExperienceOrb` / `SendRemoveActor` on the entity port (an `AddActor` of `minecraft:xp_orb` with the value in metadata, which is what PocketMine and Dragonfly send; the `SpawnExperienceOrbPacket` in the protocol docs is a client-only one-shot with no entity id, so it cannot be moved, collected or removed and is not used), plus the pickup sound. Packets only serialize.
- **Spawn.** `MobKillReward` stops crediting directly: it asks the new store to spawn the split orbs at the mob's position, with the random pop velocity Dragonfly uses (x, z in -0.2..0.2, y 0..0.4). Only a player kill spawns orbs, as today (an unattributed death grants nothing); the amount is still the species' `killExperience`. Players' own death drops experience too in vanilla (7 per level, capped at 100); that is a separate item in section 4.
- **Collection.** On the touch, the system credits `AddExperience` and sends the attributes, then removes the orb. The credit and the removal are one step in the game loop, so an orb is collected once or not at all; a test asserts the total points conserved across a crowd of orbs and players (no loss, no double credit) at every tick. A full orb store spawns what fits and credits the rest straight away, so a full store never loses experience (the same rule gravity got: capacity never deletes value).
- **Policy.** Orbs write no block and ask no policy. Region protection does not stop experience.
- **Tests.** Split sizes (every amount from 1 to 3000 sums back exactly, and uses the fewest orbs); a kill spawns orbs and credits nothing until collection; homing only inside 8 blocks; collection credits once; two players, the nearest wins and the other gets nothing; despawn at 6000 ticks without credit (vanilla, to verify); a full store credits directly; replication to a peer that joins late; level-up on collection sends the attributes once.
- **Measurement.** A farm of a few hundred orbs is the worst case; the per-tick scan is the target search once a second per orb plus the pickup box test. Measured with the existing runtime load harness before it is called done.

## 2. Falling blocks: what Zenith does now, and the gap

Zenith already has the entity: sand and gravel become a `falling_block` actor (ADR §95), with vanilla gravity and drag (0.04, 0.02), landing, persistence of a fall in flight across a restart, and region policy (plan 002). So the question "which approach" is already answered: the entity approach is the one all references use (PocketMine `FallingBlock` entity, Dragonfly `FallingBlockBehaviour`), and Zenith's is the more careful of the three. What differs is only **when** a block starts to fall and **how a stack** falls.

- **Start.** Breaking the support enqueues the block above with `TryEnqueue` (immediate) and `GravitySystem` runs after `BlockEditSystem` in the same tick, so the block becomes an entity in the very tick of the break: delay 0. PocketMine does the same (no delay, `onNearbyBlockChange` converts at once). Dragonfly does not model a delay either. Vanilla Bedrock schedules a tick when a neighbour changes and the block converts when it comes due: a delay of **2 ticks** (to verify on BDS; Java's value is 2 and Bedrock is believed to match). That is the "falls right at the next tick" you saw: it is real, and it is the same as PocketMine, but it is not vanilla. The visible effect is that the sand never hovers for the instant it does in the real game.
- **Stack.** Zenith starts at most `MaxStepsPerTick = 2` new falls per tick and defers the cell above a started fall by one tick, so a column of sand peels off one block per tick (two on the first tick). Vanilla gives every block its own scheduled tick, started by the block below leaving, so a column peels off one block per 2 ticks, evenly. Zenith's cascade is twice as fast as vanilla's, and uneven at the start.
- **Landing.** Landing on a cell that no longer accepts the block: vanilla turns it into an item; Zenith rests it above (a deliberate, documented divergence from plan 002: a block is never deleted). Not part of this change.

### Phase B: the scheduled start. DONE, see "Phase B result"

Reuse what exists: `ScheduledTicks` already holds "this cell is evaluated at tick N" for liquids and fire. A gravity block that becomes unsupported schedules itself `GravityDelayTicks` ahead (2, one named constant, set from the BDS check) instead of being enqueued at once; when it comes due, `BeginFall` re-checks support (so a block whose support came back falls no more) and runs as today. The cascade then follows by itself: the block above is scheduled when the one below leaves, so a stack peels at one block per delay with no `MaxStepsPerTick` special case. The per-tick cap stays as a safety bound (a very large collapse does not start thousands of falls in one tick), but it stops being what paces an ordinary stack.

- **Invariants kept.** Everything from plan 002 phase 4a/4b is unchanged: one representation of a block at every tick, the record in the column image before the origin is cleared, the policy question for the origin, the room check before clearing. The scheduled delay happens *before* the block is touched, so the block is simply still there, supported by nothing, for 2 ticks: nothing to persist for it (a restart in that window loses only the schedule; the block is re-evaluated when its column loads, the same as any other unsupported gravity block today, to be confirmed by a test with a crash in the window).
- **Tests.** A broken support: the block is still there for exactly the delay and falls on the next tick after it; the support restored inside the window: it never falls; a stack of five peels at one block per delay; the cap still bounds a huge collapse; the crash-in-the-window test; the plan 002 gravity tests unchanged apart from running the extra ticks.
- **Risk.** Low. It changes timing only. The constant is a number to verify, not to guess: if BDS shows a different delay, the constant changes and the tests with it.

## Phase B result

- **What changed.** `GravityPendingStore` now holds each cell with a due tick (`TryEnqueue(x, y, z, dueTick)`, a priority queue; `PromoteDue(now)` once per tick; `PromoteAll()` at shutdown). Breaking or placing schedules `now + StartDelayTicks` (2); a block that lands unsupported, or whose lower neighbour starts to fall, schedules the same way, so a stack peels one block per delay with no special case. `MaxStepsPerTick` went from 2 to 64: it is now only a bound for a huge collapse, no longer what paces a stack. The separate "deferred" queue is gone: one mechanism instead of two.
- **Reused mechanism.** The plan proposed `ScheduledTicks`; the gravity store already had the dedupe, the cap and the shutdown drain, and `BlockUpdateSystem` dispatches scheduled ticks by block kind, so the due tick went into the gravity store instead. Same idea, no second dispatcher.
- **Unchanged.** Everything from plan 002 phase 4a/4b: `BeginFall` re-checks support when the cell comes due, asks the policies, checks room, records before clearing.
- **A crash inside the delay** loses only the schedule: the column still holds exactly one block, hanging, which falls the next time something updates its neighbourhood (the same exposure the old in-memory queue had). A graceful stop settles it (`SettleAllPending` promotes everything). Stated and tested; nothing is persisted for the window.
- **Not touched, found on the way.** Explosions, liquids and support loss that remove a block under sand do not enqueue gravity at all (only player edits and falls do). That is an older gap and a separate small change.
- **The 2 is not yet checked against BDS.** One named constant, `GravityPendingStore.StartDelayTicks`.
- **Tests (`GravityStartDelayTests`).** A real break: the sand still hangs for exactly the delay, then becomes one entity. Support restored in the window: it never falls. A five-block stack: every block lands, the count is conserved at every tick, starts are at least a delay apart. A collapse of 138 cells: starts per tick bounded, all land. Graceful stop and crash in the window. Setting the delay to 0 fails two. Existing gravity tests now advance the clock, which they previously never did.

## 3. Order and cost

1. Phase B first (small: one scheduling change in `GravitySystem` and its tests, no new entity, no protocol), because it is a correction of something that already exists.
2. Phase A second (a new entity with its store, system, port methods, sound and tests; the larger change).

## 4. Related parity gaps found while reading, not part of this plan

Each is its own item with its own plan if you want it: experience from ore mining, smelting, breeding and fishing; the player's death drop of experience; orb merging; persistence of orbs; item-entity details compared with `FloorDropSystem` (merging distance, pickup delay) were not reviewed here.

## Decisions needed

- Phase B: confirm the 2-tick delay may be taken from vanilla knowledge and corrected after a BDS check, or wait for the BDS check first.
- Phase A: confirm orbs stay RAM-only (like floor drops and falling blocks), and that merging is left out of the first version.

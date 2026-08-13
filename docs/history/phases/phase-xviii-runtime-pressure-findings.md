# Phase XVIII — Gameplay Pressure Expansion & Runtime Architecture Validation Findings

This phase resolved the one gap every prior findings doc had flagged and deferred — real entity
interaction — and added one boss-shaped entity specifically to pressure-test whether behavior
complexity forces a component system. It also revisited, without adding new gameplay for the sole
purpose of forcing them, the two duplications Phase XVII left at "two instances, not a pattern
yet" (targeting, wander) and the viewer-reconciliation loop Phase XVI/XVII flagged as a growing
maintenance candidate.

Full suite: 760/760 passing (baseline 747 + 13 new: 4 `PendingValue.TryConsumeIf` tests, 3
Villager-trade tests, 2 Cow-feed tests, 6 Golem tests — counts don't sum exactly to 13 because two
Villager tests replaced/extended existing coverage; see the diff for the precise count), no
regressions.

## New gameplay added

### Priority 1 — Entity interaction (Villager trade + Cow feed)

`InventoryTransactionPacket` already decoded `TargetActorRuntimeId` and `ActorActionType`
(`ActorInteract = 0` vs. `ActorAttack = 1`) — Phase XVI and XVII's "deferred plumbing" notes were
slightly wrong about *where* the gap was. The gap was never the wire decode; it was that
`HandleInventoryTransaction` only ever branched on `ActorAttack`. `ActorInteract` was decoded and
silently dropped.

What was built:
- **`Player.SubmitInteractIntent(long targetActorRuntimeId)`** / **`TryConsumeInteractIntent(long
  expectedTargetRuntimeId)`** — a new `PendingValue<long>` mailbox, submitted from the network
  thread on `ActorInteract`, consumed on the GameLoop tick. Because the target entity id is known
  at submit time but *which system owns that id* is not known until each candidate system checks,
  `TryConsumeInteractIntent` only consumes if the pending value matches the caller's own entity —
  otherwise it leaves the intent untouched for the next system to check that same tick. This
  required one small, genuinely plumbing-only addition to the shared mailbox primitive itself:
  `PendingValue<T>.TryConsumeIf(Predicate<T>, out T)`.
- **`VillagerSystem.TryHandleInteraction`/`TryTrade`** — reach-checked interact; consumes one
  `Villager.RequestedWare` (wheat) from the player's held stack, gives one `Villager.Wares[0]`
  item (emerald) back, all-or-nothing, with a `TradeCooldownTicks` cooldown per villager.
- **`CowSystem.TryHandleFeed`** — reach-checked interact; consumes one wheat from the player's
  held stack, clears the cow's breed cooldown immediately (the "love mode" trigger Phase XVI
  explicitly said was missing).

Both consume the same `PlayerInventory.TryConsume`/`TryAdd` and
`session.Protocol.Inventory.SendInventoryContent`/`World.PersistInventory` pattern `HungerSystem`
already established for eating — no new inventory-mutation path was needed.

### Priority 4 — Golem (boss pressure test)

A new `Golem`/`GolemStore`/`GolemSystem` slice: high health (100), a health-threshold phase
transition (`IsEnraged` at ≤50%), melee (reusing `GroundMobCombat` unmodified), and — once
enraged — an area slam that damages every player within radius in one action. Targeting is a
fresh per-tick nearest-player scan with no retained field, a deliberate departure from
Zombie/Spider's retain-until-invalid shape (see Priority 2 below for why that matters).

## Priority 1 findings — does interaction become a real runtime concept?

**No new runtime concept was needed; it was a protocol dispatch gap, not a missing entity-model
abstraction.** The `TargetActorRuntimeId` was already flowing through the wire layer correctly.
Once `HandleInventoryTransaction` actually dispatched `ActorInteract`, both Villager's trade and
Cow's feed were straightforward additions inside their own systems — no `InteractionContext`,
`EntityAction`, or `Transaction` type was needed, and neither was `Interaction` as a cross-cutting
concept. The two real interaction handlers (`VillagerSystem.TryTrade`, `CowSystem.TryHandleFeed`)
share a shape at the *reach-check* level (near-identical to `GroundMobCombat
.ApplyPlayerMeleeAttacks`'s loop) but diverge completely in what they do once triggered — one
consumes-and-produces two different stacks, the other consumes-and-mutates a cooldown. That
divergence is real and is exactly why nothing was extracted: **is interaction only a protocol
concern?** No — it crossed into Player inventory mutation and Cow's breed-cooldown field, i.e.
gameplay-system-owned state, same as attack already does. **Is it gameplay-system specific?**
Yes, once past the reach check — matches the project's established shape (shared reach/consume
gate, system-owned effect). **Does it cross multiple runtime categories?** Yes, in exactly the way
attack already does (Player intent → mob-owned state), and no more broadly than that.

## Priority 2 findings — third instance validation

**No third instance appeared for either duplication, and none was manufactured.**

- **Targeting (Zombie/Spider)**: Golem was the one new mob added this phase with real
  chase-and-attack behavior, and it deliberately does *not* retain a target field —
  `GolemSystem.FindNearestPlayer` re-scans fresh every tick instead of committing to one player
  until invalid. This was a real design choice (a boss should always focus whoever is closest
  *right now*, not commit to whoever it first saw), not an attempt to dodge the extraction
  question. Zombie/Spider's retain-and-validate shape stays at exactly two instances. **Still not
  extracted** — consistent with the project's own rule stated in this phase's brief: "do not
  extract merely because two systems look similar."
- **Wander (Cow/Villager)**: Golem doesn't wander at all — it's always either chasing or, pre-
  aggro, stationary at spawn (no passive AI was built for it, since a boss standing still until a
  player is in detection range is the correct behavior, not a missing feature). No third wander
  consumer appeared. **Still not extracted.**

Both duplications remain exactly where Phase XVII left them: real, at two instances, correctly
below this project's extraction bar. The next mob that needs *actual* retained-target
chase-and-attack (not a boss re-scanning, not fuse-trigger, not damage-trigger) — or actual
proximity-free 2D wander — is still the real trigger, not this phase's Golem.

## Priority 3 findings — viewer reconciliation

Investigated, not extracted. Concretely:

- **Are the copies truly identical?** Structurally yes — all ten now (Zombie, Skeleton, Cow,
  Creeper, Enderman, Projectile, Bat, Spider, Villager, Golem) follow the identical shape:
  `ActorInterest.Includes` check → remove-and-notify on exit → add-and-send-spawn-plus-health on
  enter. Byte-for-byte copy-pasted with only the per-mob `SendAdd*` call and field names differing.
- **Are bugs caused by drift?** None found or reported across this entire project's history
  (Phases XIV–XVIII). No test failure, in this session or referenced in any prior findings doc,
  has ever traced back to a viewer-reconciliation copy diverging from its siblings.
- **Are changes repeatedly required in multiple locations?** No. Every change to replication
  behavior so far (adding despawn, adding a new mob, adding knockback) touched exactly one
  system's copy of the loop, because each mob's replication needs have stayed identical to every
  other mob's. Nothing has ever needed the loop's *behavior* to change, only its *presence* to be
  copied into a tenth file.

**Conclusion: still not extracted.** The maintenance-pressure signal is real (ten copies is a lot
to keep in sync by eye) but the specific evidence the brief asks for — measured bugs from drift,
or a change that had to touch multiple copies with different results needed in each — has not
appeared. A `ReplicationComponent`/`EntityReplicationFramework` remains unjustified. If a small
helper is ever extracted here, per the brief's own framing it should be a small helper (something
like `ViewerReconciliation.Sync(...)` taking the position, the `_replicated` set, and two
callbacks for add/remove), not a framework — but even that is not yet justified by the concrete
tests above; it is only justified by the raw count crossing ten. Flagged again for Phase XIX as
the strongest still-unresolved maintenance-pressure candidate in the project.

## Confirmed abstractions

- **`GroundMobCombat`** — reused unmodified by Villager's non-lethal-until-killed combat and by
  Golem's higher-health, higher-XP-tier combat. Nothing about a 100-health boss or an NPC with no
  retaliation required this to change shape.
- **`GroundMobMovement.CanStandAt`** — reused unmodified by Golem's chase movement.
- **`GroundMobLifecycle.EvaluateDespawn`** — reused unmodified by Golem, a sixth and seventh
  consumer respectively counting Bat (flying) from Phase XVII. Despawn remains proven
  position/visibility-based, independent of movement mode or behavior complexity.
- **`PendingValue<T>` mailbox pattern** — extended (via `TryConsumeIf`) rather than replaced to
  support a new routing need (multiple candidate owners checking one pending value). The
  extension is generic, non-gameplay, and reusable — consistent with the mailbox layer's existing
  "plumbing, not dispatch" boundary; it still does not know what a villager or a cow is.
- **`PlayerDamage.Apply`** — confirmed to already generalize to one-to-many damage (Golem's slam)
  with zero changes; it was already per-player, and nothing in its contract assumed a single
  target per ability.

## Still concrete concepts

- **`PlayerInventory` vs. `Villager.Wares`** — unchanged conclusion from Phase XVII, now exercised
  by a real trade instead of just seeded data: they still share nothing beyond both holding
  `StackId`s. The trade path reads `PlayerInventory` through its existing `TryConsume`/`TryAdd`
  API and treats `Villager.Wares` as a bare list with no analogous API of its own — it didn't need
  one.
- **`VillagerSystem.TryTrade` vs. `CowSystem.TryHandleFeed`** — both reach-checked interacts
  triggered by the same wire action and the same `TryConsumeInteractIntent` call, but one is a
  two-item exchange and the other is a single-item consume-and-cooldown-reset. Same trigger,
  different-shaped gameplay outcomes — a real example of "looks similar at the wire, is concretely
  different in gameplay," matching the project's `ActorInteract`/`ActorAttack` split itself.
- **Zombie/Spider targeting vs. Golem targeting** — both "find and pursue a player," but retained
  vs. re-scanned is a real semantic difference (commitment vs. always-current), not a stylistic
  one. A boss that keeps chasing a player who ran behind cover while a second player stands next
  to it would behave differently under each model — this is gameplay-visible, not just
  implementation detail.
- **Ground movement vs. Flight movement vs. this phase's Golem movement** — Golem reuses
  `GroundMobMovement.CanStandAt` exactly as Zombie/Spider do; it is not a new movement mode. Only
  Bat's flight remains genuinely different (Phase XVII).

## Future ECS candidates

None crossed the evidence bar this phase. Restated from the brief's own criteria, checked
explicitly against what actually happened:

- **Composition pressure**: none. Golem — the one entity built specifically to be "complex" — did
  not need an arbitrary combination of capabilities outside `IGroundMob`'s existing five members
  plus its own three extra fields (`IsEnraged`, `NextSlamTick`, `NextAttackTick`). A boss with
  phases and a second ability still fits one concrete type with no capability composition problem.
- **Data-oriented pressure**: none. No hot loop processes heterogeneous categories together; mob
  counts remain small (still no cap anywhere except `CowSystem.MaxPopulation`); nothing was
  profiled because nothing suggested a cost worth profiling.
- **Maintenance pressure**: real only for viewer reconciliation (§ Priority 3), and even there the
  brief's own bar (measured bugs, or repeated multi-location changes with different results
  needed) was not met. This is the one candidate worth revisiting first if a Phase XIX adds an
  eleventh replicated category.

## Rejected approaches

- **`InteractionContext`/`EntityAction`/`Transaction` types.** Rejected — two real interaction
  consumers exist (Villager trade, Cow feed) and share only the reach-check shape, which is
  already small enough to duplicate without an abstraction (each is ~15 lines). Building a generic
  interaction pipeline for two structurally-different consumers would be solving a problem neither
  of them actually has.
- **`GroundMobTargeting` extraction.** Rejected — still two real instances (Zombie, Spider), and
  Golem was deliberately built to be a genuine third data point on whether targeting generalizes,
  not a forced third instance. It came back "no, still two."
- **A shared `Wander` helper.** Rejected for the same reason — still two instances (Cow,
  Villager), and Golem doesn't wander at all.
- **`ReplicationComponent`/`EntityReplicationFramework`.** Rejected — ten structurally identical
  copies is real maintenance surface, but the brief's specific evidence bar (measured drift bugs,
  forced multi-location edits) was checked and not met.
- **BaseEntity hierarchy / generic Mob system / full ECS migration.** Same conclusion as every
  prior phase, reaffirmed with new evidence rather than repeated by assertion: seven mob types
  (Zombie, Skeleton, Cow, Creeper, Enderman, Bat, Spider) plus one NPC (Villager) plus one boss
  (Golem) — nine concrete `IGroundMob` implementations now — still share exactly as much as
  `IGroundMob`/`GroundMobCombat`/`GroundMobMovement`/`GroundMobLifecycle` already capture, and
  nothing more. A boss with two phases and an area ability did not need inheritance, a component
  system, or virtual dispatch to exist as one file.

## What runtime concepts became stable enough to exist after this phase's real gameplay?

**One: the interact/attack split at the wire boundary is now a real, exercised runtime concept —
not because a new abstraction was built, but because the existing decode (`ActorInteract` vs.
`ActorAttack`) turned out to already be the right shape once it was actually wired to two
consumers.** Everything else stayed exactly where Phase XVII left it: `IGroundMob`,
`GroundMobCombat`, `GroundMobMovement`, and `GroundMobLifecycle` absorbed a boss, an NPC with real
(if simple) economic behavior, and a new player-to-entity interaction path without any of them
changing shape. Two duplications (targeting, wander) remain honestly at two instances — this phase
generated a real third data point for targeting (Golem) and got a genuine "no" rather than forcing
a "yes." The viewer-reconciliation loop is the one place real maintenance pressure exists without
yet crossing this project's evidence bar, and is the most likely site of the next real extraction
if gameplay keeps growing the replicated-category count.

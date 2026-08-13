# Phase XX — Runtime Relationships, Physics Pressure & Replication Maturity Findings

This phase pressured three things prior phases had explicitly deferred or left unresolved: a real
player↔entity relationship (riding, not a one-shot push), a third distinct movement-validity model
(swimming), and the viewer-reconciliation duplication every findings doc since Phase XVI had
flagged but never re-evaluated against a raised bar. It also carried out the naming review Phase
XIX's evidence had been building toward.

Full suite: 786/786 passing (baseline 769 + 17 new: 13 Minecart/riding tests — the Phase XIX push
tests were superseded, not kept alongside — 7 Fish tests, 4 `ViewerReconciliation` leaf tests, net
count reflects the actual diff), no regressions. `IGroundMob` was renamed to `IDamageableActor`
across all ten implementers plus `GroundMobCombat`'s two generic constraints — a pure rename, zero
behavioral change, verified by the same full suite.

## Priority 1 — Real riding: does player↔vehicle control need a new runtime relationship primitive?

**No new primitive — a bidirectional pair of plain fields was enough, and the answer to "who owns
the relationship" turned out to be "both sides own their own half."**

### What was investigated before coding

Bedrock's actor-linking is `SetActorLinkPacket` (0x1b, not previously present in this codebase's
`ProtocolInfo`/`Packets`). Its wire shape (`RiderUniqueId`, `RiddenUniqueId`, `Type`,
`Immediate`, `CausedByRider`, `VehicleAngularVelocity`) was scaffolded manually
(`src/zenith/Packets/SetActorLinkPacket.cs`), following the same outbound-only, hand-written
`Encode`/no-op `Decode` pattern `AddActorPacket` already uses — not the `[GamePacket]` source-gen
attributes, since this packet is never received, only sent (mount/dismount is always
server-decided in this slice: see below).

### What was built

- **`Player.RidingEntityId : long?`** — the rider's half of the relationship. Plain mutable
  property, not a mailbox: this is server-decided relationship state, not a one-shot network
  intent (contrast with `Player.SubmitInteractIntent`, which *triggers* a mount/dismount attempt
  but does not itself represent "currently mounted").
- **`Minecart.OccupantPlayerRuntimeId : long?`** — the vehicle's half, same identity convention
  every mob's `TargetPlayerRuntimeId` already uses (a `RuntimeId`, resolved against `online` each
  tick, not a live object reference).
- **`MinecartSystem`** is the only writer of either side. `TryHandleMountInteraction` is the
  relationship's entry point (the third `ActorInteract` consumer, superseding Phase XIX's one-shot
  push — see Rejected approaches for why keeping both wasn't attempted).
  `TryReleaseInvalidOccupant`/`Dismount` are its exit points, called from every path that can end
  the relationship.
- **`MovementSystem`** gained one small, narrow carve-out: when `player.RidingEntityId is not
  null`, client-reported `AuthInput` is drained but never applied to position (same shape as the
  existing `IsDead`/`!IsInGame` early-continue branches, not a new concept). This is the entire
  "narrow vehicle-control path" the phase asked for — global player-movement authority did not
  change.
- **Steering**: while mounted, `MinecartSystem.ApplyRiderSteering` adds a small constant forward
  impulse along the rider's current `Yaw` every tick, then the existing rolling-motion/friction
  physics (unchanged since Phase XIX) carries it. The rider steers by looking, not by additional
  analog input — deliberately, see Rejected approaches for why parsing raw `AuthInput` deltas as
  drive intent was not attempted.

### The six lifecycle questions, answered concretely

- **Who owns the relationship?** Both sides, symmetrically: `MinecartSystem` is the only writer of
  both `Minecart.OccupantPlayerRuntimeId` and `Player.RidingEntityId`, but each side is queried by
  its own owner (`MovementSystem` reads `RidingEntityId`; nothing outside `MinecartSystem` reads
  `OccupantPlayerRuntimeId`). No third object represents "the relationship" — there is no
  `RiderLink`/`VehicleOccupancy` type. Two plain fields, one writer, was sufficient.
- **What happens if Player disconnects?** `TryReleaseInvalidOccupant` runs first in every tick
  before despawn/combat, checking whether the resolved occupant is still `IsInGame` and alive —
  exactly the same check every mob's target-validity logic already makes for retained targets. A
  disconnected player (still present in `online` per `NetworkSession`'s existing contract, per
  `MovementSystem`'s own comment) is auto-dismounted the very next tick.
- **What happens if Minecart despawns/is destroyed?** Both `TryDespawn` and the death branch of
  `TryApplyDamage` call `Dismount` before completing removal — an occupied cart cannot be removed
  out from under its rider. In practice, despawn while genuinely occupied by a present player
  cannot actually happen: `FollowOccupant` keeps the rider's position equal to the cart's, so the
  rider always counts as "a player nearby" for `GroundMobLifecycle`'s own purposes — an emergent,
  not designed-in, consequence worth noting.
- **What does late join observe?** `ViewerReconciliation`'s `onEnter` callback for Minecart
  (see Priority 4) checks `OccupantPlayerRuntimeId` and sends `SetActorLink` alongside the normal
  spawn/health packets — a peer who gains interest in an already-occupied cart (via late join or
  re-entering interest range) is told the mount state on the same tick it learns the cart exists.
- **Is the relationship gameplay state, protocol state, or both?** Gameplay state only.
  `SetActorLinkPacket` is sent as a *consequence* of a gameplay state change (mount/dismount/late
  discovery), the same layering as every other actor packet in this codebase — Gameplay decides,
  Protocol transmits.
- **Deterministic, exactly-once?** `Dismount` is idempotent by construction (`if
  (minecart.OccupantPlayerRuntimeId is not { } riderId) return;` at the top) — safe to call from
  multiple removal paths without double-firing `SetActorLink(Remove)` or double-counting
  `DismountCount`.

### What was deliberately not built (and why)

Real analog rider-driven steering (translating the rider's raw `AuthInput` position deltas into
drive intent) was not attempted — the client's local position while camera-locked to a vehicle is
not expected to be meaningful, and building a filter to distinguish "legitimate drive intent" from
"stale/predicted client position" would be exactly the kind of unproven mechanism this project
avoids building speculatively. Steer-by-look-direction is a real, if minimal, instance of "player
controls vehicle," sufficient to answer this priority's actual question.

**Untested against a live Bedrock client.** This project's test suite is logic-level only (no
client-in-the-loop harness exists anywhere in it); `SetActorLinkPacket`'s field order and the
assumption that a client auto-follows a linked vehicle without a separate teleport were derived
from protocol documentation cross-referencing (matching this codebase's existing practice for
`AddActorPacket`, see its own doc comment), not verified against real client behavior. This is
flagged explicitly, not silently assumed correct.

## Priority 2 — Movement mode pressure: what actually generalizes across ground/flight/swim?

**Answer: only the wander heading-picking shape and the "try a step, on failure hold position and
pick again next tick" control loop. The validity predicate itself is different in all three, on
purpose, and that is now a three-way confirmed result, not a guess.**

| Model | Predicate | Requires |
|---|---|---|
| Ground (`GroundMobMovement.CanStandAt`) | destination + one-above must be air, one-below must be solid | support underneath |
| Flight (`BatSystem.TryFly`) | destination + one-above must be air | nothing underneath |
| Swim (`FishSystem.TrySwim`) | destination cell must BE water | occupancy of a specific block type, not air at all |

Fish's `Wander`/`PickNewHeading` (3D heading, periodic re-pick, retry-next-tick on failure) is
structurally identical to Bat's — this was expected and is not new evidence (both were built to
share that shape; the interesting question was always the validity rule, not the heading-picking
loop). What Fish adds that's genuinely new: it's the first movement model whose rule is
*inclusion* in a block type rather than *exclusion* of solidity. Ground and flight are both
variations on "is there open space here" (with or without a support requirement); swim is "is this
specifically water" — a categorically different question a generalized `NavigationMode` enum
would have to paper over with per-mode special-casing anyway.

**No `NavigationMode`/`MovementComponent`/`NavigationSystem`/`PhysicsComponent` was built.** Three
real, still-separate systems remain correct: each validity rule is a two-to-four-line predicate
with no shared logic worth extracting beyond what's already shared (the retry-next-tick heading
loop, which Bat and Fish already share by direct code similarity, not a common function — still
only two instances of that specific duplication, below this project's own three-instance bar).

## Priority 3 — Impulse/knockback: did a third instance appear?

**No, and none was manufactured.** Riding's steering (`ApplyRiderSteering`) *adds* velocity every
tick while mounted (a continuous force, not a one-shot impulse-then-decay), and swimming has no
velocity concept at all (`Fish` moves by direct position assignment in `TrySwim`, matching Bat's
shape, not Zombie's/Minecart's). Neither is a third instance of the specific
impulse-then-multiplicative-decay-then-negligible-zero shape Zombie's knockback and Minecart's
(pre-riding) push shared. That duplication remains at exactly two instances — Zombie's knockback
(still real, still triggered by a landed hit) and Minecart's rolling-motion decay (still present;
riding changed what *adds* velocity, not the decay/friction step itself, which is unchanged from
Phase XIX). Consistent with the project's standing rule: two instances is not yet a pattern, and no
gameplay this phase manufactured a third to force the question.

## Priority 4 — Viewer reconciliation: has it matured into a small shared primitive?

**Yes — this phase's one real "yes" to an extraction question, made by explicitly re-evaluating
the evidence bar rather than reapplying the old one unchanged.**

### The audit

Twelve consumers were compared side by side field-by-field (relevance predicate, replicated-key
shape, enter behavior, spawn projection, health/state projection, leave behavior, remove
projection, cleanup behavior, movement-replication relationship): Zombie, Skeleton, Cow, Creeper,
Enderman, Projectile, Bat, Spider, Villager, Golem, Minecart, Fish. Result: **all twelve were the
exact same seven-step shape** — relevance check via `ActorInterest.Includes`, remove-key-and-notify
on exit, add-key-and-notify on enter — differing in literally nothing structural, only in what
"notify" sends (which `SendAddX` call, whether `SendHealth` is included — Projectile skips it,
correctly, since it has no `HealthState` — whether a `_lastProjected` move-projection cache needs
clearing, and Minecart's one extra `SetActorLink` send this phase added).

### The bar, re-evaluated

Phase XVI through XIX applied one bar: *no measured drift bug, no forced multi-location edit with
different results needed in each*. That bar was never crossed — and it still hasn't been, by that
narrow reading. But the brief's own instruction was explicit: eleven-then-twelve byte-identical
copies is itself meaningful maintenance evidence, and blindly preserving a bar calibrated for "is
this two suspicious-looking mobs" indefinitely, as the count keeps growing purely from adding
entities (not from any decision to duplicate this shape on purpose), stops being the right
question. The actual test applied here: **would a small, honestly-scoped helper reduce real
surface area without hiding a decision that needs to stay visible per mob?** For this specific
loop — yes. Every "decision" in it (which packet to send, whether to send health, what to clean
up) already lives entirely in the caller's lambda; the loop itself decides nothing gameplay-shaped.

### What was extracted

`ViewerReconciliation.Sync` (`src/zenith/Gameplay/ViewerReconciliation.cs`) — no type parameter, no
interface, not even a marker interface for "damageable actor": it takes a bare `long entityId`,
because `Projectile` (which has no `IDamageableActor`-shaped health/damage contract) is a real
consumer too and a marker interface would have either excluded it or been meaningless overhead.
Relevance is a caller-supplied `Func<Player.Player, bool>` — the helper does not know
`ActorInterest` exists as a specific policy, only that "relevant" is a caller decision. Enter/exit
are caller-supplied `Action<Player.Player>` callbacks. All twelve systems were refactored to call
it; the refactor is a pure extraction (identical generated behavior), verified by the unchanged
full test suite passing before and after.

### What stayed a deliberate non-decision

`ActorInterest.Includes` itself was not touched or generalized — it remains the concrete interest
policy for these twelve, while Floor Drop's chunk-knowledge gate and Falling Block's unconditional
broadcast remain their own, different, un-migrated mechanisms (per Phase XVI's original finding,
never revisited because no evidence suggested those two should ever share this policy). This is
explicitly not a `ReplicationSystem`: nothing decides *when* something is relevant, only *what to
do once relevance is known* — a narrower, safer boundary than the brief's caution against a
framework required.

## IGroundMob naming review

**Renamed to `IDamageableActor`.** The contract's actual members — `EntityId`, `RuntimeId`,
`PositionX/Y/Z`, `Health`, `IsActive`, `ApplyDamage`, `Remove` — describe exactly one thing:
identity, position, the ability to take damage, and a removal lifecycle. Nothing about walking,
nothing about AI, nothing "mob" or "ground" about it. By this phase it had ten real implementers,
several of them neither ground nor mob by any honest reading: `Bat` (flying), `Villager` (an NPC),
`Minecart` (a vehicle with zero AI), `Fish` (swimming). The old name was not merely imprecise — a
new contributor reading `IGroundMob` on `Fish` or `Minecart` would reasonably conclude something
was wrong with the code.

The name chosen — `IDamageableActor` — was picked specifically to avoid the broader names the
brief warned against (`Entity`, `IEntity`, `LivingEntity`, `Actor` alone). "Actor" is already this
codebase's own wire-layer vocabulary (`AddActorPacket`, `RemoveActorPacket`, `ActorRuntimeId`,
`ActorInterest`) — reusing it here is borrowing an existing, narrow, already-scoped word from this
project's own vocabulary, not reaching for a new ontology term. "Damageable" is the qualifier that
keeps it from overreaching into "any replicated thing" (Floor Drop and Falling Block are both
replicated `Actor`-shaped things in the wire sense but have no `HealthState`/`ApplyDamage` and
correctly do not implement this interface).

**What was deliberately NOT renamed**: `GroundMobCombat`, `GroundMobMovement`, and
`GroundMobLifecycle` keep their existing names. `GroundMobMovement` is correctly scoped as-is — it
is genuinely ground-specific and nothing (Bat, Fish) uses it. `GroundMobCombat` and
`GroundMobLifecycle` are, by the same argument as the interface, now used by non-"ground-mob"
consumers (Minecart, Villager, Bat, Fish) and are arguably stale in the same way `IGroundMob` was.
They were left alone this phase for a narrower reason than "avoid churn": the brief's own naming
review was scoped explicitly to `IGroundMob` by name, and renaming three more classes' worth of
call sites (a materially larger blast radius than one interface) without an equally explicit
mandate risks exactly the kind of naming churn this project's methodology has consistently avoided
doing speculatively. This is flagged as unfinished, not settled — see Phase XXI candidates.

## Runtime store pressure

**Not extracted. No operational or maintenance trigger appeared.**

Twelve stores were re-audited (`ZombieStore` through `FishStore`, plus `ProjectileStore`): all are
still the same ~10-line `List<T>` wrapper (`Active`, `TryAdd`, `Remove`), and `ProjectileStore`
alone still has a `SoftCap` none of the others need. Checked directly against the brief's three
questions:

- **Do they now need common capacity behavior?** No — `CowSystem.MaxPopulation` remains a
  system-level cap tied specifically to breeding, not a store-level concept; no other store has
  ever needed a cap.
- **Does runtime tooling/diagnostics need to enumerate them?** No — `ZenithServer`'s tick observer
  sums `.Active.Count` from each store by name, one line per store. This is a real, if trivial
  (one line, added once per new entity type), duplication, but it is at the composition root, not
  inside the stores themselves, and has never needed a store to expose anything beyond `Active`.
- **Has any cross-store operation appeared?** No — every system still iterates exactly one store.
  No feature has ever needed "all mobs regardless of species" as a single collection.
- **Would a generic store make a future data-oriented migration easier or harder?** Harder, on
  reflection. A real ECS-shaped migration replaces per-species `List<T>` stores with component
  arrays entirely — it would not reuse a generic `Store<T>` wrapper, it would delete stores as a
  concept. Building one now would be intermediate scaffolding for a migration that has no other
  evidence behind it (see ECS re-evaluation below), adding a layer that would need to be unwound
  rather than reused if that migration ever actually happens.

## ECS / hybrid re-evaluation

- **Composition pressure**: none. Riding added two plain fields to two existing types
  (`Player.RidingEntityId`, `Minecart.OccupantPlayerRuntimeId`) — no entity needed an arbitrary
  capability combination that didn't fit its existing concrete shape. Fish fit `IDamageableActor`
  with zero new capability combinations beyond Bat's already-established shape.
  Player↔Minecart relationship processing is two plain nullable fields read by their respective
  owners, not a new object requiring composition.
- **Data pressure**: none. No system this phase needed to process Player + Minecart + Mob +
  Projectile together in one pass. `MinecartSystem` iterates only its own store; `MovementSystem`'s
  new carve-out is a single `if` per player already being iterated for other reasons, not a new
  cross-category loop.
- **Maintenance pressure**: real, and for once acted on — viewer reconciliation crossed the
  (re-evaluated) bar this phase and was extracted. Stores did not. Naming was materially
  misleading and was corrected for the interface; the helper classes' naming remains open.

**No inheritance was introduced anywhere in this phase.** Riding is two fields and one writer
system, not a `Rideable`/`Mount` base type. Swimming is a third concrete validity predicate, not a
`NavigationMode` enum or a `SwimmingMob` subtype. The runtime model is unchanged in shape:
Identity + Data + System-owned behavior.

## Rejected approaches

- **A generic `ActorLink`/`RiderRelationship`/`VehicleOccupancy` type.** Rejected — two plain
  nullable fields with one writer answered every lifecycle question the brief posed (ownership,
  disconnect, destruction, late join, determinism) without needing a third object to represent
  "the relationship" itself.
- **Making player movement server-authoritative globally.** Rejected — the narrow
  `RidingEntityId`-gated carve-out in `MovementSystem` was sufficient and keeps every other
  player's movement exactly as client-authoritative as before this phase.
- **Real analog rider-driven steering from raw `AuthInput` deltas.** Rejected as unproven and
  fragile to build speculatively; steer-by-look-direction answers the same architectural question
  with a mechanism this codebase can actually reason about server-side.
- **`NavigationMode`/`MovementComponent`/`NavigationSystem`/`PhysicsComponent`.** Rejected — three
  real movement-validity predicates remain genuinely different in what they check (support
  requirement, air requirement, specific-block-type requirement), and the only shared shape
  (heading-pick-and-retry) is still at two instances (Bat, Fish), below this project's bar.
- **Manufacturing a third impulse/knockback instance.** Rejected outright — the brief itself
  warned against this, and riding's continuous steering force and Fish's velocity-free swim are
  both genuinely different mechanisms, not disguised third instances.
- **A generic `Store<T>`.** Rejected — no operational trigger exists, and a real future
  data-oriented migration would replace stores rather than reuse a generic wrapper; building one
  now is scaffolding for a migration with no other evidence behind it.
- **Renaming `GroundMobCombat`/`GroundMobMovement`/`GroundMobLifecycle`.** Deferred, not rejected
  outright — `GroundMobMovement` is correctly scoped; the other two are arguably stale the same way
  `IGroundMob` was, but renaming them was out of this phase's explicit naming-review scope. Flagged
  for Phase XXI.
- **`ReplicationSystem`/`ReplicationComponent`/a generic replication framework.**
  Rejected even while extracting `ViewerReconciliation.Sync` — the helper owns membership
  bookkeeping only, takes no ownership of interest policy or gameplay decisions, and stays
  deliberately smaller than "system."

## Definition of Done — answered

1. **Does player↔vehicle control require a new runtime relationship primitive?** No — a
   bidirectional pair of plain fields with one writer was sufficient for every lifecycle question
   posed.
2. **What actually generalizes across ground/flight/swimming movement?** Only the
   heading-pick-and-retry control shape (already at two instances, Bat/Fish, below the extraction
   bar). The validity predicates themselves are three genuinely different rules and remain three
   separate, concrete functions.
3. **Did impulse-plus-decay reach a legitimate third consumer?** No, and none was manufactured;
   riding and swimming both turned out to be different mechanisms entirely.
4. **Has viewer reconciliation finally matured into a small shared primitive?** Yes —
   `ViewerReconciliation.Sync`, extracted after an explicit side-by-side audit of twelve consumers
   and a deliberate re-evaluation of the evidence bar itself, not just a re-application of it.
5. **Is `IGroundMob` now materially misnamed?** Yes, and it has been renamed to
   `IDamageableActor` — a narrower, more precise name that reuses this project's own existing
   "Actor" vocabulary rather than reaching for a broader ontology term.
6. **Did any real ECS/hybrid trigger finally appear?** No. Composition, data-oriented, and
   maintenance pressure were all checked explicitly against this phase's real gameplay (riding,
   swimming, a twelve-consumer audit) and none crossed the bar except the one already acted on
   (viewer reconciliation, resolved with a five-parameter static function, not a component
   system).

The current runtime still scales — and after this phase, that conclusion is supported by a real
player↔vehicle relationship, a third movement model, and a maintenance-pressure candidate that was
actually resolved rather than deferred for a fifth time.

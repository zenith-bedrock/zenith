# Phase XIX — Gameplay Expansion & Runtime Pressure Continuation Findings

This phase deliberately expanded outside the mob category for the first time (Minecart, a
vehicle) and added the project's first mob-sourced status effect (Spider poison), while
continuing to only monitor — not force — the two duplications (targeting, wander) and the
viewer-reconciliation maintenance signal every findings doc since Phase XVI has tracked.

Full suite: 769/769 passing (baseline 760 + 9 new: 7 Minecart tests, 2 Spider poison tests), no
regressions.

## Gameplay added

### Priority 1 — Minecart (entity category beyond mobs)

A new `Minecart`/`MinecartStore`/`MinecartSystem` slice, built specifically to NOT fit the
existing five runtime categories cleanly: no AI, no targeting, no wander, no gravity — it never
chooses a destination for itself. It only moves because a player pushes it
(`MinecartSystem.TryHandleInteraction`, the third real `ActorInteract` consumer after Villager's
trade and Cow's feed, Phase XVIII), then coasts to a stop under friction using the exact
impulse-plus-decay shape Zombie's Phase XVI knockback already established
(`ApplyPushImpulse`/`ApplyRollingMotion` mirror `ApplyKnockbackImpulse`/`ApplyKnockbackMotion`
structurally, with different constants). Combat/loot/XP/despawn/replication all reuse
`GroundMobCombat`/`GroundMobLifecycle`/`ActorInterest` unmodified — it implements `IGroundMob`
even though it is definitely not a "ground mob" in the gameplay sense.

### Priority 2 — Spider poison-on-hit (cross-system pressure: status effects)

A landed Spider bite now has a 30% chance to apply Poison to the player
(`SpiderSystem.TryApplyPoison`). This is the project's first status effect ever applied by
something other than a player-submitted `EffectIntent`. It required **zero changes** to
`EffectSystem`: that system's `TickActiveEffects` already iterates `player.Effects` and ticks
whatever `EffectType`s are present, without recording or caring who put them there.
`TryApplyPoison` calls the exact same `Player.ApplyOrRefreshEffect` (an `internal` method, already
callable from any file in this assembly) that `EffectSystem.ApplyPendingIntent` calls for a
player-submitted intent, then sends the same `MobEffectPacket`. From that point on, `EffectSystem`
ticks the Poison exactly as if a potion had applied it.

## Priority 1 findings — does the category split still represent reality?

**Yes, and Minecart sharpened rather than blurred it.** The five-category table in
[docs/entities.md](../../entities.md) §1 already distinguished categories by *lifecycle, persistence,
and replication mechanism* — not by "what it looks like." Minecart's lifecycle (despawn-when-
unseen via `GroundMobLifecycle`) and replication (`ActorInterest`) are identical to every ground
mob's, so it correctly sits in that category's table row despite being conceptually a vehicle, not
a mob. This is the same finding Bat produced in Phase XVII (movement mode ≠ runtime category) and
Golem produced in Phase XVIII (behavior complexity ≠ runtime category) — Minecart is the third
confirmation that `IGroundMob`/`GroundMobCombat`/`GroundMobLifecycle`'s actual scope is "any
position-having, health-having, despawn-eligible, interest-replicated actor," which is
substantially broader than "mob" but has not needed to change to accommodate any of the three.

**No new runtime category emerged.** A genuinely new category would need a lifecycle, persistence,
or replication mechanism none of the existing five use — Minecart doesn't have one; it reuses
ground-mob mechanisms exactly. The brief's explicit warning ("do not merge categories because they
have Position/Lifetime/Replication — those similarities were already proven insufficient") cuts
the other way here too: Minecart isn't being *merged* into Ground Mob by fiat, it *already*
qualifies by the same lifecycle/replication test every other row in the table was built from. What
makes it different — no AI, no self-chosen destination, movement only from external push — is
gameplay-category information, not runtime-category information, exactly mirroring how "Mob" was
already established as a gameplay category laid over the `IGroundMob` runtime contract, not a
runtime category itself.

**What a real second vehicle would need to test, that this one didn't:** whether "player controls
this entity's movement directly, in real time" (actual riding, not a one-shot push) requires new
runtime plumbing. That was explicitly scoped out — see Rejected approaches.

## Priority 2 findings — does interaction crossing into effects need a new concept?

**No.** The chain Player-attacks-Spider → Spider-lands-hit → Spider-applies-Poison →
EffectSystem-ticks-Poison crosses Combat, a new mob-authored write to Player state, and the
existing Effects system, and none of those three needed a shared "status" abstraction to cooperate.
`EffectSystem`'s boundary (own `player.Effects`, tick whatever's in it) turned out to already be
the right shape for "any source can add an effect" — it was written in Phase XI.3 for
player-submitted intents and never needed to assume that was the only source. This is the same
kind of result as Golem's slam proving `PlayerDamage.Apply` was already one-to-many-capable
(Phase XVIII): a boundary drawn narrowly around *what data it owns* rather than *who is allowed to
write to it* generalizes for free.

## Priority 3 — continued monitoring, no extraction

- **Targeting (Zombie/Spider)**: still exactly 2 instances. Minecart has no targeting at all
  (nothing to chase). Not extracted.
- **Wander (Cow/Villager)**: still exactly 2 instances. Minecart doesn't wander (it is inert
  unless pushed — a fourth distinct "how does this move" shape alongside ground chase, 3D flight,
  and passive wander). Not extracted.
- **Viewer reconciliation**: now duplicated an eleventh time (`MinecartSystem.ReconcileViewers`
  is byte-for-byte the same shape as the other ten). Checked again against the brief's evidence
  bar: no measured drift bugs, no gameplay change yet forced edits across more than one or two
  copies. Still not extracted, still the strongest concrete maintenance-pressure candidate in the
  project, now with an even larger raw count.

## Confirmed abstractions

- **`GroundMobCombat`** — a fourth non-"mob" consumer (Minecart, after Villager/Golem/Bat already
  established the contract is broader than its name) with zero changes needed.
- **`GroundMobMovement.CanStandAt`** — reused unmodified for Minecart's rolling-step validity, the
  same predicate ground-chase mobs use for walking steps.
- **`GroundMobLifecycle.EvaluateDespawn`** — an eighth consumer (Minecart), still
  position/visibility-based regardless of what "moves" the entity.
- **`Player.Effects` / `EffectSystem`'s tick loop** — confirmed to already generalize to
  non-player-sourced effects with zero code changes, the same way `PlayerDamage.Apply` was
  confirmed to already generalize to multi-target damage in Phase XVIII.
- **The knockback/push impulse-plus-decay shape** (Zombie's Phase XVI knockback) — reused as a
  *pattern* (not a shared function — still two independent implementations, `ZombieSystem`'s and
  `MinecartSystem`'s, with different constants and different trigger conditions: landed melee vs.
  interact) for Minecart's push physics. This is a real, observed second instance of that
  specific shape; not yet extracted, consistent with the two-is-a-coincidence rule, and flagged
  below as a new pressure point.

## Still concrete concepts

- **Minecart's push vs. Zombie's knockback vs. Golem's (absent) player-knockback.** Three related
  but distinct velocity-impulse situations: Zombie's knockback is damage-triggered and pushes the
  *mob*; Minecart's push is interact-triggered and pushes the *vehicle*; Golem's slam explicitly
  does not push the *player* at all (Phase XVIII, deliberately not built — player movement is
  client-authoritative). These are not one concept wearing different names; they differ in what
  gets pushed and why, and in whether server-authoritative movement of the pushed thing is even
  possible (mob/vehicle: yes, server owns their position; player: no, client does).
- **`SpiderSystem.TryApplyPoison` vs. `EffectSystem.ApplyPendingIntent`.** Both ultimately call
  `Player.ApplyOrRefreshEffect` and send the same `MobEffectPacket`, but they are triggered by
  completely different things (a landed mob attack vs. a player-submitted intent) and live in
  different systems. They share a call, not a code path — no `Interaction`/`EffectSource`
  abstraction was needed to make that sharing work.
- **The three `ActorInteract` consumers** (Villager trade, Cow feed, Minecart push) — still three
  structurally-different effects triggered by the same reach-checked wire action, exactly as
  documented in Phase XVIII. Minecart's addition doesn't change that conclusion; it reinforces it.

## New pressure points

- **The push/knockback impulse-plus-decay shape now has two real instances** (Zombie's landed-hit
  knockback, Minecart's interact-push). Per the project's own rule, this is not yet a pattern —
  but it is the first candidate since Phase XVII's targeting/wander pair to reach two real
  instances, and is worth watching for a third (a mob that should be knocked back by an
  explosion, or a second pushable object) before Phase XX.
- **Viewer reconciliation is now at eleven copies**, the highest it has been. Still not crossing
  the brief's evidence bar, but each new mob/entity phase adds roughly one more copy — this is the
  most likely site of an actual extraction in a near-future phase if the pattern continues, purely
  from raw count pressure even absent a measured bug.
- **A second vehicle, or actual player-riding, remains untested.** This phase deliberately scoped
  Minecart down to a one-shot push rather than real-time rider control specifically to avoid
  building unproven server-authoritative-player-movement machinery speculatively. If real riding
  is ever wanted, it is the next concrete place a genuinely new runtime concept (a
  player-controls-entity link, analogous to Bedrock's `SetActorLinkPacket`) could become
  justified — but only once that gameplay is actually being built, not before.

## Rejected approaches

- **Real player-riding / `SetActorLinkPacket` mounting.** Rejected for this phase specifically:
  building bidirectional player-vehicle control would require new server-authoritative movement
  plumbing (player position currently client-authoritative everywhere else in this codebase, per
  docs/entities.md's capability matrix) that no other feature needs yet. A one-shot push interact
  answers the category-split question this priority actually asked without speculatively building
  that machinery.
- **`StatusComponent`/`EffectComponent`.** Rejected — the existing `Player.Effects` +
  `EffectSystem` boundary already generalized to a mob-sourced effect with zero changes. Building
  a component for a boundary that already works would be solving a problem that didn't appear.
- **A shared `Impulse`/`Knockback` primitive.** Rejected — still only two real instances (Zombie,
  Minecart), below the extraction threshold, and flagged as a new pressure point above rather than
  extracted prematurely.
- **`GroundMobTargeting`/shared `Wander` extraction.** Rejected again — Minecart added no third
  instance of either (it has neither targeting nor wander at all). Still exactly two instances
  each, per Phase XVII/XVIII.
- **`ReplicationComponent`/`EntityReplicationFramework`.** Rejected again — eleven copies is a
  larger number than Phase XVIII's ten, but the brief's specific evidence bar (measured drift
  bugs, forced multi-location edits with different results needed) still has not been met.
- **BaseEntity hierarchy / generic Mob system / full ECS migration.** Same conclusion, reaffirmed
  with a fourth and fifth kind of new evidence this phase (a non-mob vehicle, a cross-system status
  effect): ten `IGroundMob` implementations plus one first-of-its-kind status-effect interaction
  still share exactly what `IGroundMob`/`GroundMobCombat`/`GroundMobMovement`/`GroundMobLifecycle`
  already capture, nothing more, and a vehicle with zero AI fit that contract as cleanly as a boss
  with two phases did.

## After expanding gameplay beyond mobs, which runtime concepts became stable enough to exist?

**None new.** That is the finding, stated plainly for the third phase in a row: `IGroundMob`,
`GroundMobCombat`, `GroundMobMovement`, and `GroundMobLifecycle` absorbed a vehicle with none of
the "mob" characteristics (no AI, no health-driven behavior beyond being destroyable, movement
sourced entirely from outside itself) without changing shape, and `EffectSystem`/`Player.Effects`
absorbed a non-player effect source without changing shape either. What this phase's evidence
actually supports is narrower and more useful than "no new concepts ever": the runtime layer
(`IGroundMob` + its four helpers) was never really about mobs — it was about "positioned, health-
bearing, replicated, despawn-eligible things," and that description was broad enough from the
start to include a vehicle. The two real, still-below-threshold duplications from Phase
XVII (targeting, wander) remain exactly where they were; a third real one (impulse-plus-decay) has
now appeared alongside them, at exactly the same "two instances, watch for a third" stage. Nothing
here argues for designing a runtime differently than it already is designed.

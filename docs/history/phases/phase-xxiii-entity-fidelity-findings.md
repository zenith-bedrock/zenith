# Phase XXIII — Entity Fidelity (in progress)

This phase is large (52 parts in the originating brief) and was worked incrementally rather than in
one pass. This document records what was actually done so far, in priority order per the brief's own
Part 52 ("research → hurt/death feedback → look/yaw → per-species tuning → ... → real-client
validation → performance → docs"). Current living state (matrix, conventions, gaps) lives in
`docs/entity-fidelity.md`; this file is the append-only research/decision record.

## Baseline

`dotnet build zenith.sln --no-restore` clean, `dotnet test zenith.sln --no-restore` at 861/861 before
any Phase XXIII change (carried over from Phase XXII's closing state plus the protocol-2168 fix
commit). After this session's changes: 880/880, still 0 failures.

## Research pass (Part 1-2)

Reference hierarchy actually used, in the order the brief specifies:

- **Tier 1 (direct client observation)**: not performed. No live Bedrock client was available in this
  environment this session. This is the phase's largest open gap — see "Known gaps" in
  `docs/entity-fidelity.md`.
- **Tier 2 (local Bedrock references, `D:\Development\bedrock`)**: `gophertunnel` (Go) used as the
  primary protocol-correctness source — already established as reliable in the prior protocol-2168
  review session. Confirmed exact-version match (`CurrentProtocol = 2168`).
- **Tier 3 (Java-facing / NMS-adjacent reference)**: `PowerNukkitX` (Java, ports Bedrock protocol with
  Java-parity-intentioned AI) inspected for `LookController.java` — real evidence, not architecture to
  copy.
- **Tier 4 (plugins)**: not consulted this pass; no unique evidence need arose that Tier 2/3 didn't
  already answer.

### Findings from research

1. **Protocol id bug**: `SetActorLinkPacket` was wired to `0x1b` since Phase XX. Cross-checking
   gophertunnel's `packet.id.go` iota table shows `0x1b` (27) is actually `IDActorEvent`; real
   `IDSetActorLink` is 41 (`0x29`). This is a genuine wire-corruption bug for a packet already in
   active use (Minecart mount/dismount) — a real client would misparse the payload. Fixed by
   reassigning the enum values in `ProtocolInfo.cs`.
2. **`ActorEventPacket` was entirely missing.** Needed for hurt/death visual feedback (Part 18-19).
   Wire shape (`EntityRuntimeID` unsigned varlong, `EventType` byte, `EventData` zigzag varint32,
   optional `Vec3` fire-position) confirmed against gophertunnel's `packet.ActorEvent.Marshal`. Event
   id constants (`Hurt = 2`, `Death = 3`) confirmed against PocketMine's `HurtAnimation.php` /
   `DeathAnimation.php`, which both encode exactly `ActorEventPacket::create(id, ActorEvent::HURT_ANIMATION|DEATH_ANIMATION, 0, null)`
   — i.e. no extra data, no fire-position, matching what Zenith now sends.
3. **PowerNukkitX's `LookController`** separates body yaw (route/movement direction) from head yaw
   (look-at target) as two independent fields, confirming Part 10's desired direction is a real,
   implemented pattern elsewhere — but it does **not** bound turn rate; both snap instantly per tick
   in that reference. This tempered how much confidence to assign the bounded-turn-rate change below.
4. **Existing Zenith yaw math was duplicated identically** across five systems
   (`Zombie`/`Skeleton`/`Spider`/`Cow`/`Minecart`), all `MathF.Atan2(-dx, dz) * (180f / MathF.PI)` —
   exactly the pattern Part 8 warned about and pre-authorized extracting a shared helper for (past the
   "≥3 real consumers" bar). Extracted to `LookMath`.

## Hurt / death feedback (Part 18-21, 24-25) — done this session

- `ActorEventPacket` implemented; `EntityProtocol.SendHurt`/`SendDeath` added.
- Wired into `DamageableActorCombat` (all 5 ECS-damageable species) and `GroundMobCombat` (all 6
  legacy species) at the exact point health is already replicated — one shared boundary, not six
  per-species insertions. Also wired into `PlayerDamage`/`PlayerVisibility` for player-vs-player hurt/
  death.
- **Part 21 resolved**: no "dying" ECS lifecycle state was introduced. `SendDeath` fires immediately
  before `SendRemoveActor`; PocketMine's reference treats death as a one-shot event tolerating
  immediate removal, and nothing in the gophertunnel wire format suggests otherwise. `EntityRuntime.DestroyActor`
  stays immediate.
- Tests: `ActorEventPacketTests.cs` (wire-shape proof, independent of gameplay) and two new
  `ZombieSystemTests` cases (`Non_lethal_damage_to_a_replicated_zombie_sends_a_hurt_reaction_and_leaves_it_alive`,
  `Lethal_damage_sends_death_reaction_before_removal_and_destroys_the_zombie`) proving the shared seam
  actually fires for at least one ECS species end-to-end.

## Look / yaw correctness (Part 7-9, 32, 38) — done this session

- `LookMath` (`src/zenith/Gameplay/LookMath.cs`) — `YawTowards`, `NormalizeYaw`, `ShortestYawDelta`,
  `MoveYawTowards`. Documents Zenith's yaw convention once instead of re-deriving it per call site.
- `DefaultMaxTurnDegreesPerTick = 10f` — explicitly labeled MEDIUM confidence (see
  `docs/entity-fidelity.md` "Turning"); this is the phase's clearest example of "do not invent precise
  values" tension: the value is defensible from general Minecraft experience but not measured against
  a live client or found in any local reference.
- Wired into Zombie (`AdvanceTowardTarget`), Skeleton (`TryRangedAttack`), Spider
  (`AdvanceTowardTarget`), Cow (moved from a one-time snap in `PickNewHeading` to a per-tick smoothed
  turn in `Wander`, since the old code only touched yaw once per wander leg — smoothing only at pick
  time would have left the cow facing a stale angle for the rest of the leg).
- Minecart deliberately excluded — its yaw legitimately should snap to its actual rolling velocity
  every tick (a vehicle has no independent "look"), so `ApplyRollingMotion` is unchanged.
- `LookMathTests.cs` — 14 tests, including the wraparound case the brief's Part 38 explicitly
  requires: 179°→-179° must turn ~2° (the short way), not ~358°.
- Full suite re-run after wiring: 880/880, no regressions from the bounded-turn change (existing tests
  didn't assert exact per-tick yaw snapping).

## Legacy actor look/turn migration (Part 9, follow-up)

After the user chose to keep validating with a real client independently while non-client-dependent
work continued, the five remaining legacy actors with duplicated instant-snap yaw math
(Creeper/Golem/Villager/Bat/Fish) were migrated to `LookMath.MoveYawTowards` the same way
Zombie/Skeleton/Spider/Cow were — this was pure code, no client evidence needed. Villager/Bat/Fish had
the same Cow-shaped bug (yaw only touched once per wander leg inside `PickNewHeading`, not every tick
during `Wander`); fixed the same way, moving the yaw update into the per-tick movement method.

**Enderman was deliberately left alone** — it never sets `.Yaw` at all (its teleport-based movement
changes position but never orients the actor), and fixing that needs an actual behavior decision (face
target on aggro-teleport vs. random on passive-teleport, and whether that should be instant like the
teleport itself or smoothed) rather than a mechanical `LookMath` wire-up. Documented as an open gap in
`docs/entity-fidelity.md` instead of guessed.

Full suite re-run after this pass: 880/880, no regressions.

## Movement-replication rotation bug (Part 10/44 research follow-up) — the largest finding this session

While investigating Part 10 (does Bedrock support body/head yaw separately in `MoveActorAbsolute`,
confirmed yes via gophertunnel's `Rotation mgl32.Vec3` field), reading `EntityProtocol.cs`'s actual
non-player movement path (`RawActorPose`, `SendMoveActorAbsoluteRaw`/`SendMoveActorAbsoluteRaws`)
revealed those types never had Pitch/Yaw/HeadYaw fields at all — `MoveActorAbsolutePacket`'s rotation
fields silently defaulted to `0f` on every ongoing movement update for every mob system using this
path (10 of 12 implemented species: everything except the player-move path and Skeleton, which
currently never moves). `AddActor` at spawn sent the correct initial yaw once; the very next movement
tick would silently reset it to face +Z regardless of true heading, for the rest of the actor's life.

This is very plausibly a root cause (or a major contributor) to the brief's own opening problem
statement — "mobs do not consistently look toward their target correctly," "turning can appear
instantaneous or unnatural" — independent of and likely more impactful than the turn-smoothing work
above, since a mob that gets its facing reset to a fixed direction every tick would look broken
regardless of how smoothly the underlying yaw value itself changes.

**Fix**: added `Pitch`/`Yaw`/`HeadYaw` to `RawActorPose`, threaded through both
`SendMoveActorAbsoluteRaw` (added optional parameters, default `0f` preserved for the two callers that
genuinely have no orientation — `GravitySystem`'s falling blocks, `ProjectileSystem`'s arrows, which
gophertunnel's own doc comment notes use the third rotation component as roll, not head yaw, for
projectiles) and `SendMoveActorAbsoluteRaws`. Updated all 10 real mob-movement call sites
(Zombie/Cow/Spider/Creeper/Golem/Villager/Bat/Fish/Minecart/Enderman) to pass their actual current
yaw. Full suite: 880/880, no regressions — none of the existing tests asserted on wire-level rotation
content, so this bug had zero test coverage before being found by code reading, consistent with the
brief's Part 2 instruction to treat source as authoritative over assumption.

**Follow-up fix (same session)**: the per-peer move-dedup check only compared X/Y/Z, not yaw, across
seven independently-duplicated `ProjectedPosition` structs (Zombie/Cow/Creeper/Golem/Spider/Villager/
Minecart) — a mob turning in place without crossing the position epsilon would have its yaw update
silently dropped even after the fix above. Extracted the duplication into a shared `ProjectedPose`
(`src/zenith/Gameplay/ProjectedPose.cs`, past the "≥3 real consumers" bar for the same reason
`LookMath` was) with a yaw-delta check (`LookMath.ShortestYawDelta`, `0.5°` epsilon) added alongside
the position check. Full suite: 880/880 (one flaky pre-existing parallel-execution test —
`SpiderSystemTests.Spider_retargets_when_retained_player_becomes_invalid`, unrelated shared-state race
in `Tools.EnsureLoaded`, passes isolated — confirmed unrelated to this change).

## Skeleton spacing behavior (Part 27 follow-up)

User chose to implement this despite it being a new behavior rather than a bug fix, since it directly
addresses a brief-called-out identity gap: "Skeleton should not behave like Zombie that fires
projectiles... its ranged spacing is part of its identity." Before this, Skeleton never moved at all
after spawn — only rotated to aim shots.

Added `MaintainRange` (runs every tick, before `TryRangedAttack`): always turns to track the target's
yaw (even off shot-cooldown, matching Part 12's "should visibly track the relevant target"); retreats
if the target closes inside 5 blocks; approaches if the target is beyond 10 blocks (but still within
the existing 20-block detection range); holds position and only tracks otherwise. Distances are
explicitly LOW-confidence placeholders establishing the mechanism, not measured vanilla values — see
`docs/entity-fidelity.md`.

This required adding the full move-replication machinery Skeleton never had (`_moveBatch`,
`_lastProjected`, `ReplicateMoves`, `TryMove` against `GroundMobMovement.CanStandAt`, plus cleanup in
`TryDespawn`/`ReconcileViewers`/`TryApplyDamage`'s `onDeathReplicatedToPeer`) — mirroring the
established Zombie/Spider shape rather than inventing a new one. `TryRangedAttack`'s own yaw-setting
was removed since `MaintainRange` now owns yaw every tick unconditionally.

Five new tests (`A_player_closing_inside_the_retreat_range_pushes_the_skeleton_back`,
`A_distant_player_within_detection_but_beyond_the_approach_threshold_pulls_the_skeleton_closer`,
`A_player_within_the_preferred_band_is_held_at_range_not_approached_or_retreated_from`, plus the two
pre-existing tests) — full suite: 883/883. Mixed-roster benchmark re-run (1200 actors): avg 8.5ms
(was 7.9ms), p99 22.6ms (was 26.7ms, within run-to-run noise) — still far under the 50ms tick budget.

## Not yet done (remaining scope, in the brief's priority order)

4-7. Zombie/Skeleton/Spider/Cow individual species tuning (movement speed vs measured vanilla,
   attack windup/recovery, idle look, target retention specifics) — blocked on Tier-1 (live client)
   evidence for anything beyond what code inspection already gives.
8. Shared projection cleanup beyond what Parts 18-21/7-9 already touched.
9. Legacy actor (Creeper/Enderman/Bat/Villager/Golem/Fish) spot checks — `docs/entity-fidelity.md`
   already flags they still instant-snap yaw; not migrated to `LookMath` this session (lower priority
   than the ECS-authoritative four).
10. Real-client validation (Part 36) — explicitly requires either a live Bedrock client run against
    Zenith or the user's own observation; not something this environment can perform unattended.
11. **Re-run** (Part 45): `--mixed-roster --actors 1200 --actor-players 10 --ticks 200` after this
    session's changes: avg 7.9ms (was 6.7ms), p99 26.7ms (was 14.7ms), still far inside the 50ms tick
    budget. The brief explicitly says not to require exact equality due to machine/JIT variance, but
    the p99 jump is large enough to be worth another look in a future pass rather than dismissing
    outright — plausibly the extra `ActorEvent` packet per non-lethal hit plus the bounded-turn math
    running every tick for four species, not yet profiled to confirm which. Not optimized this session
    per Part 45's own "do not optimize prematurely" instruction — recorded as an open question, not a
    blocker.
12. Head/body/pitch separation (Part 10) — not modeled; every species still sends one yaw as both body
    and head orientation.
13. Attack timing audit (Part 17) — damage still applies on `distance <= range && cooldown == 0`
    everywhere; no windup/recovery phase exists for any species.

## Final Questions — answered so far

1. **Yaw convention**: documented in `docs/entity-fidelity.md` — yaw 0°=+Z, clockwise from above,
   `(-180, 180]`.
2. **Which yaw calculations were incorrect?** None were mathematically wrong; the formula was correct
   but instantly-snapping and duplicated five times.
3. **Body/head/pitch distinction?** Not yet implemented; Bedrock does distinguish them (confirmed via
   `AddActorPacket`'s already-separate rotation fields and PowerNukkitX's `LookController`), but Zenith
   currently projects one yaw as both.
9. **Dying visual state vs immediate ECS destruction?** Immediate destruction is kept — see "Hurt /
   death feedback" above.
19. **Is fidelity good enough to move to WorldGen?** Not yet — this is an early, partial pass. Hurt/
    death feedback and turn smoothing are real, tested improvements, but movement-speed research,
    attack timing, head/body separation, and all real-client validation remain open.

All other numbered questions remain open pending further work in this phase.

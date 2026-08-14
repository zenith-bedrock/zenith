# Phase XXIII-B — Entity Fidelity Validation, Combat Timing & Vanilla Motion Polish (in progress)

Continuation of Phase XXIII. This is not a new architectural phase — it takes the projection/bug
fixes from the first XXIII pass and drives them toward observable fidelity using real-client
evidence. Full original brief recorded below for reference; findings are appended as validation
rounds complete. Living state (matrix, conventions, gaps) stays in `docs/entity-fidelity.md`; the
practical test flows for gathering evidence live in `docs/entity-fidelity-validation.md`.

## Brief scope (as received)

Priority actors: Zombie, Skeleton, Spider, Cow (deep pass), with shared-projection verification on
Creeper, Golem, Enderman, and the rest of the roster.

Core rule: **do not tune values blindly** — real-client observations from the user are the required
input before further tuning happens (Part 2 of the brief). For every reported issue, classify as one
of `BUG | TUNING | MISSING VANILLA BEHAVIOR | PROTOCOL / PROJECTION | LOCAL NAVIGATION | UNKNOWN —
REQUIRES RESEARCH`, then resolve in that order where practical.

33 parts total, grouped by theme:

- **Part 1** — re-baseline (build/test, review the ~27-file working-tree diff from XXIII-A).
- **Part 2** — consume real-client feedback first; record Actor/Observed/Expected/Cause/Confidence/Fix/Retest-needed per item in the living doc, not just chat history.
- **Part 3** — validate the rotation-projection fix (`ProjectedPose`/`RawActorPose` carrying yaw) actually works live: stationary rotation, moving rotation, direction changes, stop-and-continue-looking, ±180° crossing, for Zombie/Skeleton/Spider/Cow/Creeper/Golem. If an actor still snaps/resets, inspect projection (spawn yaw, packet yaw, headYaw, pitch, metadata, dedup, flags, tick ordering) before touching AI.
- **Parts 4-6** — head yaw vs body yaw: research and implement a minimal look model (movement influences body, look target influences head, large divergence triggers body catch-up) using researched values, not the brief's own illustrative pseudocode. Pitch/vertical look for Skeleton aiming, Zombie/Spider looking up at elevated targets — only if it visibly helps.
- **Part 7** — exact movement-speed pass: measure Zenith's current blocks/second for Zombie/Spider/Cow/Skeleton in flat-ground scenarios, compare against the best available reference, produce a before/reference/after/confidence table. Normalize to `BlocksPerSecond` config-level, convert to per-tick internally.
- **Part 8** — movement character beyond top speed: instant start/stop, sliding, oscillation, sideways full-speed movement — only add acceleration/deceleration if evidence shows instant displacement is visibly the problem.
- **Part 9** — Zombie as the reference-quality melee hostile: targeting, pursuit, attack (windup/damage-frame/cooldown as distinct concepts), damage reaction, all validated end-to-end.
- **Part 10** — attack-animation research (client-inferred vs explicit ActorEvent/AnimatePacket/metadata).
- **Part 11** — attack windup/damage-frame research; only add explicit state if evidence supports it.
- **Part 12** — Spider must be distinguishable from "faster Zombie + Poison" — speed/turn/range/cadence/orientation audited species-specifically.
- **Part 13** — validate the new Skeleton approach/hold/retreat spacing across far/medium/close/approaching/retreating/lateral-crossing targets; add hysteresis if real-client testing exposes threshold oscillation.
- **Part 14** — audit Projectile initial aim direction (horizontal-only vs target elevation).
- **Part 15** — Cow as the reference passive mob: wander speed/duration, idle duration, turn timing, random-look, player-look, feed, damage reaction.
- **Part 16** — idle look behavior if mobs still look lifeless when stationary; small feature-owned state only, no `LookComponent`.
- **Part 17** — Creeper fidelity spot pass (approach/look/turn/fuse trigger-cancel-timing-visual/hurt/death/explosion) without necessarily a full ECS rewrite.
- **Part 18** — Enderman investigation (idle facing, look-at-player, aggro look, movement facing, teleport orientation) — research before implementing, deliberately excluded from the blanket `LookMath` migration in XXIII-A.
- **Part 19** — real-client validation of the hurt/death `ActorEvent` work from XXIII-A; revisit the "immediate ECS destruction, no Dying state" decision only if real client behavior contradicts it.
- **Part 20** — knockback fidelity across player-melee→Zombie/Cow/Spider and projectile→mob; a shared impulse helper only if ≥3 real consumers share identical semantics.
- **Parts 21-22** — local-navigation validation via deterministic obstacle scenarios (flat pursuit, single block, two-block wall, corner, doorway, one-block step, small pit, target around a short obstruction); classify failures as turning bug / collision-probe bug / local-steering weakness / requires-pathfinding. Only declare pathfinding necessary with documented evidence across several scenarios — never silently turn this phase into a pathfinding engine effort.
- **Part 23** — continue using `D:\Development\bedrock`; prefer observable Bedrock behavior > Bedrock-oriented implementations > Java/NMS/Paper > plugins (only when they preserve vanilla behavior); record reference/learning/Bedrock-vs-Java-derived/confidence per borrowed behavior.
- **Part 24** — extend the deterministic trace harness for reproducible tick/pose/target/state capture if useful; test/debug-only, not production logging on hot paths.
- **Part 25** — an optional dev-only diagnostic command surfacing per-actor id/pose/target/state, using existing diagnostics conventions, not a generic inspection framework.
- **Parts 26-27** — re-run the mixed-roster benchmark after behavior changes; avoid duplicate per-tick player scans (resolve target once, feed movement/look/attack decisions from it).
- **Part 28** — review `EntityProtocol`'s naming/boundaries after this round of animation/projection work; refine if awkward, no animation framework.
- **Part 29** — a small per-species tuning-constant grouping (e.g. `ZombieMotionProfile`) is acceptable if it actually clarifies scattered constants — not a `MobDefinition` registry or data-driven AI.
- **Parts 30-31** — keep the vanilla parity matrix in `docs/entity-fidelity.md` honest; `SIMPLIFIED` is an acceptable resting state, not something to escalate to `PARITY` without evidence.
- **Part 32** — no incidental ECS migration of the remaining legacy actors unless fidelity work substantially rewrites one anyway.
- **Part 33** — commit gate: do not commit until real-client validation of the major visual changes is complete; then build/test/benchmark/review diff, and commit as one coherent milestone or a small coherent set (suggested but not mandatory grouping: protocol/rotation fix, look/movement fidelity, combat behavior/feedback, docs).

Nine exit questions gate the move to Phase XXIV (World Generation): does Zombie look/move/attack/react
credibly; does Skeleton read as a ranged mob, not a turret; is Spider materially distinct from Zombie;
does Cow look alive idle/wandering; are hurt/death reactions visually correct; are body/head rotations
correct enough that entities don't appear to slide/stare wrong; are remaining movement defects mostly
navigation rather than basic fidelity bugs; is anything severe enough to block world-gameplay work; can
the rest evolve alongside future gameplay without another dedicated entity phase.

## Progress so far (code-only work, no live-client feedback needed)

**Attack-swing animation** — researched and implemented before any real-client feedback arrived,
since it's pure protocol research (Part 10's first question). `AnimatePacket` is documented in
gophertunnel as player-only; PocketMine's `ArmSwingAnimation` confirms non-player `Living` entities
use `ActorEventPacket` with `ARM_SWING` (event id 4, cross-checked against bedrock-protocol PHP's
`ARM_SWING` constant and gophertunnel's `ActorEventStartAttacking` — same numeric id, different
naming). Implemented `EntityProtocol.SendAttackSwing`, wired into Zombie/Spider/Golem's
`TryAttackPlayer` at the moment a melee hit lands. Test added (`ActorEventPacketTests`). Full suite:
886/886.

## Validation round 1

User tested the build and reported six findings. Full classification/fix table lives in
`docs/entity-fidelity.md`'s "Validation round 1" section (living doc, kept there rather than
duplicated here). Summary:

1. **Head not moving independently of body** — confirmed real, MISSING VANILLA BEHAVIOR, deferred to
   a dedicated Part 4-6 pass (not rushed into this round).
2. **Mobs damage Creative players** — BUG, `GameMode` was never checked in `PlayerDamage.Apply` at
   all. Fixed, with Void damage kept as the deliberate vanilla-matching exception.
3. **"Hitbox (bbox) placeholder"** — two real issues bundled together. Visual hitbox (Width/Height
   metadata) was genuinely missing for every species except Zombie/Bat — fixed with real Bedrock
   per-species dimensions (Minecraft Wiki hitbox table). Combat *reach* is still a copy-pasted
   `2.25f` constant across species — not fixed this round, recorded as an open gap.
4. **Loot doesn't visually disappear after pickup** — investigated at length; wire format and
   runtime-id bookkeeping check out against gophertunnel. Leading hypothesis: stale floor-drop
   entities left over from the *previous* session's now-fixed `Snapshot()` crash (same still-running
   server process). Needs retest on a fresh server restart before concluding whether it's resolved.
5. **Container never reopens after first close** — BUG, root cause confirmed: real Bedrock clients
   sometimes send `ContainerClose.WindowId = 0xFF` instead of the real id (documented in
   PocketMine's `InventoryManager::onClientRemoveWindow`, "since 1.21.100 and probably earlier").
   Zenith's exact-match validation silently dropped that close, permanently locking
   `player.OpenContainer`. Fixed by treating `0xFF` as "close whatever is open," matching
   PocketMine's own workaround.
6. **Golem attacks unprovoked** — MISSING VANILLA BEHAVIOR, confirmed via Minecraft Wiki research
   (naturally-spawned Iron Golems are passive until provoked by an attack on themselves or on a
   nearby villager). Zenith's Golem was a deliberate Phase XVIII "boss" design (always-hostile,
   enrage/slam) — revised to passive-until-provoked, using a proximity+recency signal
   (`Player.LastVillagerAttack`) in place of the village-reputation system Zenith doesn't have.

Full suite: 897/897 after all six fixes (one test suite update needed: `GolemSystemTests`'
always-chases-nearest-player test described the now-intentionally-removed old behavior, replaced
with four tests covering passive/self-defense/villager-provoke/out-of-range).

## Blocked on further real-client feedback

Everything else in this brief — Parts 2-9, 12, 13, 15, 17-21 explicitly, and most of the rest
implicitly — requires the user's real-client observations before proceeding, per the brief's own Part
2 gate ("do not tune values blindly before incorporating the real-client observations"). A practical
test-flow document (`docs/entity-fidelity-validation.md`) was produced to make gathering that
evidence structured and reproducible rather than ad hoc. Next step: user runs those flows, reports
findings in the requested format, and this document's "Validation round 1" section gets filled in
before any further tuning happens.

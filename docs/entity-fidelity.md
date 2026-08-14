# Entity fidelity

Living reference for how close Zenith's mobs are to vanilla Bedrock behavior — coordinate/movement
conventions, hurt/death feedback, and per-species tuning. Phase history and full research notes live
in `docs/history/phases/phase-xxiii-entity-fidelity-findings.md`; this document describes current
runtime truth, not how it got there.

## Coordinate and yaw convention

Reverse-engineered from the pre-existing (pre-Phase-XXIII) movement code and centralized in
[`LookMath`](../src/zenith/Gameplay/LookMath.cs) rather than changed:

- Yaw `0°` faces `+Z`.
- Yaw increases **clockwise** viewed from above: `+X` is `-90°`, `-Z` is `±180°`, `-X` is `+90°`.
- Range is `(-180, 180]`; `LookMath.NormalizeYaw` wraps into it.
- `LookMath.YawTowards(dx, dz) = atan2(-dx, dz) * (180/π)`.

This was confirmed self-consistent with a pre-existing test fixture (`owner.Yaw = -90f; // +X` in
`ProjectileSystemTests`), not invented this phase.

## Movement units

Every species' movement constant is a per-tick displacement (`MovePerTick`, blocks/tick) at Zenith's
fixed 20 TPS `GameClock`. Convert with `blocks/second = MovePerTick * 20`.

| Species | `MovePerTick` | blocks/second (×20) |
|---|---|---|
| Cow (wander) | 0.05 | 1.0 |
| Villager | 0.04 | 0.8 |
| Golem | 0.07 | 1.4 |
| Fish | 0.06 | 1.2 |
| Creeper | 0.08 | 1.6 |
| Zombie (chase) | 0.08 | 1.6 |
| Bat | 0.10 | 2.0 |
| Spider (chase) | 0.11 | 2.2 |

**Not yet compared against measured vanilla values** — no live-client measurement has been performed
(see "Known gaps" below). Do not retune these from this table alone.

## Movement-replication rotation bug (found and fixed this phase)

`RawActorPose`/`EntityProtocol.SendMoveActorAbsoluteRaw(s)` — the non-player actor movement-update
path used by every mob system (`MoveActorAbsolutePacket`, `0x12`) — never carried Pitch/Yaw/HeadYaw at
all; those `MoveActorAbsolutePacket` fields defaulted to `0f`. `AddActor` (spawn) correctly sent the
actor's real yaw once, but **every subsequent movement update overwrote it back to yaw=0 (facing +Z)**,
for every species using this path (Zombie/Cow/Spider/Creeper/Golem/Villager/Bat/Fish/Minecart/
Enderman). A mob would appear to spawn correctly oriented, then visually snap to face +Z on its first
move update and stay that way regardless of actual travel direction for the rest of its life — a
plausible root cause for the brief's own problem statement ("mobs do not consistently look toward
their target correctly"). Confirmed by reading the code, not by symptom; fixed by threading each
system's actual `pos.Yaw` through `RawActorPose` into every batch/single move call.

Player movement (`AbsoluteActorPose`/`MoveActorAbsolutePacket` via the player-move path) was already
correct — this bug was isolated to the non-player actor path.

**Not yet covered by a decoded-wire-content test** — matches this codebase's existing convention for
movement packets (`TickBatchEgressTests` also only asserts datagram counts, not decoded rotation
bytes); the fix itself is a straightforward field-copy, verified by code reading.

## Turning

`LookMath.MoveYawTowards` bounds body-yaw rotation to `LookMath.DefaultMaxTurnDegreesPerTick = 10°`
per tick, always via the shortest angular path (verified against 179°→-179° wraparound in
`LookMathTests`). Wired into Zombie/Skeleton/Spider/Cow every tick they update yaw.

**Confidence: MEDIUM.** 10°/tick is a reasonable placeholder based on general observed Minecraft
behavior (mobs visibly turn, they don't snap), not a measured constant — no live client or Java/NMS
turn-rate source was found in the local `D:\Development\bedrock` references (PowerNukkitX's
`LookController.java` snaps yaw instantly too; it was evidence for head/body separation, not turn
rate). Needs real-client validation (Part 36) before being called PARITY.

**Minecart is the deliberate exception** — its yaw still snaps instantly to its velocity vector every
tick (`MinecartSystem.ApplyRollingMotion`). A cart has no independent "look"; vanilla carts visually
face the direction they're actually rolling, which is correct without smoothing.

**Head vs body yaw**: not yet split. All migrated species currently have one yaw value (body yaw),
sent as both body and head orientation. See "Known gaps."

## Attack-swing animation (Phase XXIII-B)

`AnimatePacket` is documented in gophertunnel as player-only ("sent by the server to send a **player**
animation... to all viewers of that player"). PocketMine's `ArmSwingAnimation` confirms non-player
`Living` entities instead use `ActorEventPacket` with `ARM_SWING` (event id 4 — same numeric id in
both bedrock-protocol PHP, which names it `ARM_SWING`, and gophertunnel, which names it
`ActorEventStartAttacking`). Added `EntityProtocol.SendAttackSwing`, wired into
Zombie/Spider/Golem's `TryAttackPlayer` at the moment a melee hit lands on a player (mirrors the
hurt/death seam: gameplay decides, protocol projects).

## Hurt / death feedback

Added Phase XXIII. `ActorEventPacket` (`0x1b`) with `EventHurt = 2` / `EventDeath = 3`, wire shape
and event ids cross-checked against gophertunnel (`packet.ActorEvent.Marshal`, protocol 2168) and
PocketMine's `HurtAnimation.php`/`DeathAnimation.php`.

- `EntityProtocol.SendHurt`/`SendDeath` — Protocol-layer projection only; gameplay decides via
  `DamageResult`.
- Wired into the two shared damage seams that cover all twelve implemented species at once:
  `DamageableActorCombat` (ECS: Zombie/Minecart/Cow/Skeleton/Spider) and `GroundMobCombat` (legacy:
  Creeper/Enderman/Bat/Villager/Golem/Fish).
- Player-vs-player damage: `PlayerVisibility.RelayHurt`/`RelayDeath`, called from `PlayerDamage.Apply`.
- Death event is sent immediately before `SendRemoveActor` — no "dying" visual-lifecycle state was
  introduced. PocketMine's reference behavior treats `DeathAnimation` as a one-shot event that
  tolerates immediate removal; Zenith's ECS `DestroyActor` therefore stays immediate (Part 21's
  question resolved without new lifecycle states).

### Protocol id correction

`SetActorLinkPacket` (mount/dismount) was wired to `0x1b` since Phase XX, which is actually
`ActorEvent`'s id (confirmed against gophertunnel's `packet.id.go` iota table). Real `SetActorLink`
is `0x29` (41). Fixed in Phase XXIII — `ActorEventPacket` now correctly owns `0x1b`.

## Species fidelity matrix

| Species | Look/turn | Hurt feedback | Death feedback | Movement speed | Attack timing |
|---|---|---|---|---|---|
| Zombie | CLOSE (bounded turn, MEDIUM confidence) | PARITY (ActorEvent) | PARITY (ActorEvent, immediate removal) | UNKNOWN vs vanilla | SIMPLIFIED (distance+cooldown, no windup) |
| Skeleton | CLOSE (bounded turn, now moves — previously never did; retreat/approach thresholds LOW confidence, unmeasured) | PARITY | PARITY | UNKNOWN | SIMPLIFIED (no windup; shot cadence untouched this pass) |
| Spider | CLOSE (bounded turn) | PARITY | PARITY | UNKNOWN | SIMPLIFIED |
| Cow | CLOSE (bounded turn, per-tick not per-wander-leg) | PARITY | PARITY | UNKNOWN | N/A |
| Creeper | CLOSE (bounded turn) | PARITY | PARITY | UNKNOWN | SIMPLIFIED (fuse timing not researched this pass) |
| Golem | CLOSE (bounded turn) | PARITY | PARITY | UNKNOWN | SIMPLIFIED |
| Villager | CLOSE (bounded turn, per-tick not per-wander-leg) | PARITY | PARITY | UNKNOWN | N/A |
| Bat | CLOSE (bounded turn, per-tick not per-wander-leg) | PARITY | PARITY | UNKNOWN | N/A |
| Fish | CLOSE (bounded turn, per-tick not per-wander-leg) | PARITY | PARITY | UNKNOWN | N/A |
| Minecart | PARITY (intentionally instant, matches vehicle physics) | PARITY | PARITY | UNKNOWN | N/A |
| Enderman | MISSING (never sets yaw at all — teleport doesn't orient the actor toward anything) | PARITY | PARITY | UNKNOWN | SIMPLIFIED |

## Projectile self-hit fix (found while investigating the crash above)

While chasing the client-crash reports, found a real, separate bug: `ProjectileSystem.FindHitDamageableActor`
never excluded the shooter's own entity from its cross-species hit query (`Query.With(Health, Position)`).
Player-fired arrows never showed this because Player isn't an ECS entity, so this query could never
match the shooter in that case — but Skeleton (the one ECS-damageable species that also fires
projectiles) could and did hit itself on the arrow's very first movement step (it spawns almost
exactly at the shooter's own position), killing itself a tick after spawn. Fixed by passing
`state.OwnerRuntimeId` through and excluding that entity from the candidate query. This was not
ultimately the cause of the client-crash reports below (see the metadata fix), but is a real,
independent correctness fix kept regardless.

## Entity metadata DX fix — real client crash found (Phase XXIII-B)

The hitbox fix above (`WriteMobDimensionMetadata`) shipped with a real bug: its declared leading
entry count said `3` while 4 entries (Flags, Scale, Width, Height) were actually written. A real
Bedrock client uses that count to know how many key/value entries to read from the metadata
dictionary — a wrong count desyncs its parser from that point in the packet stream onward, silently
misinterpreting every packet after it on the connection until something forces a disconnect (the
exact disconnect point varied between test runs because it depended on whatever bytes happened to
follow, not the actual cause).

Found via live-client testing (two crash logs, neither showing a consistent culprit at the packet
level — the common factor was *timing*, always mid-way through the spawn burst that first uses this
writer). Root-caused by re-reading the writer, not by symptom.

**Fix — matching PocketMine's design, not just this one call site.** Confirmed against
bedrock-protocol's `CommonTypes::putEntityMetadata`: PocketMine's `EntityMetadataCollection` never
hand-types a count at all — callers `set(key, value)` into a collection, and the wire count is always
`count($metadata)` at write time. Ported that shape as `EntityMetadataBuilder`
(`src/zenith/Packets/EntityMetadata.cs`): a small fluent builder (`.Flags(...).Scale().Dimensions(...)`)
whose `WriteTo` always writes `_entries.Count`, never a literal. Every `Write*Metadata` method in the
file was rewritten on top of it — the count-vs-entries mismatch bug class is now structurally
impossible, not just fixed at this one call site. `EntityMetadataBuilderTests`-style coverage proves
the count always matches for arbitrary entry combinations, not just the specific case that broke.

## Validation round 1 (real-client feedback)

Six findings from the user's first real-client pass, classified and resolved:

| Actor | Observed issue | Classification | Fix |
|---|---|---|---|
| All | Head doesn't move independently of body | MISSING VANILLA BEHAVIOR | Not yet implemented — see "Known gaps"; this is Part 4-6 work (a real head/body look model), deferred as its own follow-up rather than rushed. |
| Player | Mobs could damage a Creative-mode player | BUG | `PlayerDamage.Apply` never checked `GameMode` at all. Fixed: Creative is immune to ordinary damage; Void is the deliberate vanilla-matching exception (falling out of the world still kills Creative players, ADR §40/§73). |
| All mobs | "Hitbox (bbox) placeholder" | BUG/SIMPLIFIED | Two distinct things bundled in this report: (1) **visual** hitbox — confirmed real: every mob except Zombie/Bat sent Scale-only metadata, no Width/Height at all, so the client fell back on undefined dimensions. Fixed with real per-species Bedrock hitbox values (`WriteMobDimensionMetadata`). (2) **combat reach** — confirmed still a placeholder: `AttackDistance = 2.25f` is copy-pasted across nearly every species (same value `PlayerMeleeSystem` uses for player-vs-player), not derived from real per-species size. Not fixed this round — a real AABB-based reach system is a larger cross-cutting change; recorded as an open gap. |
| Floor drops | Picked-up items don't visually disappear from the ground | UNKNOWN — likely stale state | Investigated at length; the pickup/removal code path (`TakeItemActorPacket`, `FloorDropStore`) checks out correctly against gophertunnel's wire format and Zenith's own runtime-id bookkeeping. Leading hypothesis: this is residual "ghost" floor-drop entities left in `FloorDropStore` from the *previous* session's now-fixed `Snapshot()` crash bug (items that got added to inventory before the crash but never got their removal signal sent, in the same still-running server process). **Needs retest on a freshly restarted server** to confirm whether it's actually resolved or a distinct bug. |
| Inventory/chests | Closing then reopening a container never works again | BUG (confirmed root cause) | Real Bedrock clients send `ContainerClose.WindowId = 0xFF` ("unknown") in some situations instead of echoing the real window id — documented in PocketMine's `InventoryManager::onClientRemoveWindow` ("since 1.21.100 and probably earlier"). Zenith's exact-match validation silently rejected that as a stale close and never cleared `player.OpenContainer`, permanently locking every future container open. Fixed: `0xFF` is now treated as "close whatever is currently open," matching PocketMine's documented workaround. |
| Golem | Attacks the player unprovoked | MISSING VANILLA BEHAVIOR | Confirmed via Minecraft Wiki research: naturally-spawned Iron Golems are passive until a player attacks the golem itself (self-defense) or attacks a villager near it. Zenith's Golem was built in Phase XVIII as an intentional "boss" pressure-test (always-hostile, enrage/slam at low health) — a real, documented design choice at the time, now revised. Fixed: Golem stays passive until provoked by either trigger; retains that player as its target the same way Zombie/Spider do. Zenith has no village/reputation system, so the "attacked a villager" trigger uses proximity + a 10s recency window (`Player.LastVillagerAttack`) instead of Minecraft's full popularity/reputation model — documented as SIMPLIFIED, not PARITY. |

## Known gaps

- **No live-client visual validation performed** (Part 36) — every CLOSE/MEDIUM rating above needs
  real-client confirmation before it can be called PARITY.
- **Head yaw / pitch not modeled separately from body yaw** for any species (Part 10).
- **Enderman never sets yaw at all** — its passive/aggro teleport moves position but never orients
  the actor. Fixing this needs a real behavior decision (face the target on aggro-teleport? face a
  random direction on passive-teleport? snap instantly since a teleport is itself instant, rather than
  smoothed via `LookMath`?), not just wiring existing math — left as an open, documented gap rather
  than guessed.
- **Movement speeds not compared against measured vanilla values** — current table is Zenith's
  existing constants only, converted to blocks/second for legibility, not retuned.
- **Attack windup/recovery timing** not modeled — damage still applies on `distance <= range && cooldown == 0`
  for every species, which Part 17 explicitly flags as insufficient if vanilla has visible windup.
- **Skeleton's retreat/approach distances (5 and 10 blocks) are unmeasured placeholders** (LOW
  confidence) — they establish the spacing *mechanism* (Skeleton now moves at all, tracks its target's
  yaw continuously, retreats/approaches/holds), not tuned vanilla distances.
- **Idle look behavior** (random look-around when not moving/targeting) not implemented for any
  species.
- **Client-inferred vs explicit animation audit** (Part 41 — walking/attack animation) not performed.
- **Combat reach is not a real per-species hitbox** — `AttackDistance = 2.25f` (or close to it) is
  reused verbatim across nearly every mob system, not derived from actual entity size. Visual hitbox
  (Width/Height metadata) is now correct per species; the *gameplay* reach check is not.
- **Golem's villager-reputation trigger is proximity + recency, not real reputation** — no village or
  popularity/reputation system exists in Zenith. `Player.LastVillagerAttack` (10s window, checked
  against the golem's detection radius) is the smallest signal that reproduces the observable
  behavior (attack a villager near a golem → it comes after you), not Minecraft's actual mechanic.
- **Head/body yaw split is not implemented** — every species still sends one yaw as both body and
  head orientation (Part 4-6 of the brief); confirmed by real-client feedback as the next highest-value
  fidelity gap.

## Cross-reference audit pass (reference-server comparison)

Requested pass: compare Zenith against independent reference server implementations across combat,
mob AI timing, wire protocol, and inventory/containers, then fix what was found. Four parallel
research passes, one per area; findings below, confirmed bugs fixed in the same pass.

**Fixed:**

- **No hit-invulnerability anywhere (player or mob) — real bug, HIGH confidence.** Vanilla suppresses
  a repeat hit for ~10 ticks (0.5s) after landing one, unless the new hit is strictly harder, in which
  case it still lands. Zenith had no such state anywhere — `HealthState.Apply` just subtracted every
  time, unconditionally. Fixed in `HealthState` itself (`_invulnerableUntilTick`/`_lastDamageTaken`),
  Void bypasses it (matches vanilla — falling out of the world still damages every tick). Threading
  the acting tick through every damage call site was the bulk of this change (`PlayerDamage.Apply`,
  `GroundMobCombat`/`DamageableActorCombat.TryApplyDamage`, `DamageDispatch`, every mob system's
  `TryApplyDamage`, `ProjectileSystem`, `MovementSystem`, `EffectSystem`, `HungerSystem`,
  `PlayerMeleeSystem`). `PlayerMeleeSystem` (player-vs-player) turned out to have no knockback wired
  in at all either — fixed as part of the same pass since the direction vector was already computed
  and unused.
- **`SetActorLinkPacket` wrote Rider/Ridden in the wrong order — real wire bug, HIGH confidence.**
  The Ridden (vehicle) unique id must be written FIRST, then the Rider (passenger). Zenith wrote
  Rider first — the file's own doc comment claimed the opposite order had been verified, but hadn't
  been. Every mount/dismount link Zenith has ever sent (Minecart riding) was backwards on the wire.
- **Projectile gravity was 0.03/tick, vanilla is 0.05/tick — HIGH confidence.** Corrected; Skeleton's
  ballistic-arc launch formula (added in the prior pass) references this constant symbolically, so
  the fix keeps both consistent without re-deriving the arc math by hand.
- **Creeper ignite/defuse used one shared distance threshold — real flicker bug, MEDIUM confidence.**
  A creeper standing at exactly ~3 blocks from its target could toggle Ignited on/off every tick.
  Fixed with hysteresis (ignite at ≤3, defuse at ≥7).

**Investigated, not fixed this pass (real gaps, larger scope — documented here rather than rushed):**

- **Enderman is missing both its signature vanilla mechanics** — stare-triggered aggro and
  water/rain avoidance. Aggro is currently damage-only.
- **Villager has no flee-on-hit behavior** — vanilla villagers run from their attacker; Zenith's
  `VillagerSystem` only records the hit for Golem-provoke purposes and keeps wandering normally.
- **No per-item max-stack-size table** — every item caps at 64 uniformly
  (`PlayerInventory.MaxStack`); vanilla varies (16 for stackable-but-fragile items, 1 for
  tools/armor/buckets/etc.). No stack-size data exists anywhere in Zenith's item registry yet: adding
  it is a real feature (a data table plus every merge/transfer/split call site), not a one-line fix.
- **No horizontal drag on projectiles** — only gravity decays velocity; vanilla arrows also lose
  horizontal speed every tick. Left alone this pass since no exact vanilla drag coefficient was
  confirmed from a reliable source (avoiding an invented constant per this doc's own rule).
- **Despawn logic has no instant-despawn-at-extreme-range or distance-scaled random despawn** —
  Zenith's despawn is a uniform 5-minute no-player-nearby timer for every species, not vanilla's
  128-block hard cutoff / 32-block scaling chance.
- **Mob knockback's decay shape differs from vanilla's composition** (impulse-then-per-tick-decay vs.
  halve-existing-then-add-impulse) — LOW confidence this reads as a bug rather than a feel
  difference; not changed.

**Confirmed clean (no bug found):** the full `ProtocolInfo` packet-id table cross-checked
byte-for-byte against a trusted protocol reference; `EntityMetadata` key/type/flag constants;
`AddActorPacket`, `SetActorDataPacket`, `ActorEventPacket`, `SetActorMotionPacket`,
`MobEquipmentPacket`, `MobArmorEquipmentPacket`, container packet field order/types; every
count-prefixed list in `Packets/*.cs` (all count from the live collection, none hand-maintained);
`ArmorMitigation`'s 4%-per-point/20-point-cap formula; chest lid multi-viewer ref-counting;
window-id/generation-based stale-transaction rejection on containers.

## Cross-reference audit pass, round 2 (world/blocks, networking, session, survival)

Same method, four more areas: world/chunks/blocks, RakNet transport, player session lifecycle, and
survival/progression systems. Session lifecycle came back almost entirely clean (login sequence,
disconnect cleanup, duplicate-join handling, vehicle release, NaN/Infinity input validation were all
already correct) — only one real gap there, noted below.

**Fixed:**

- **Player-vs-player melee had no knockback at all** — found and fixed in round 1's combat pass,
  listed here for completeness since it's a `PlayerMeleeSystem` change.
- **RakNet: retransmission was NACK-only — a lost NACK permanently stalled the connection.** HIGH
  confidence, HIGH impact. If the one NACK datagram reporting a loss was itself dropped by UDP
  (exactly as likely as any other datagram), the sender never learned and the backed-up FrameSet
  sat forever, blocking that entire reliable-ordered channel. Fixed: `RakNetSession.Tick` now also
  resends any backup entry unacknowledged past a fixed 1.5s timeout, independent of NACK receipt.
- **RakNet: abandoned split-packet reassembly never expired.** HIGH confidence, HIGH impact. A
  split id whose final fragment never arrived (lost fragment, mid-transfer disconnect) occupied its
  slot forever; after 32 such abandoned reassemblies accumulated over a session's life, the session
  could no longer receive *any* further split packet. Fixed: a 30s sweep evicts abandoned entries,
  run from the same single-threaded receive path that already owns this state (deliberately not
  from the locked `Tick()` path — `FragmentsQueue` is otherwise only ever touched unlocked from the
  receive thread, and adding a second, cross-thread access point would have been a new race).
- **RakNet: 24-bit FrameSet sequence wraparound wasn't handled.** MEDIUM confidence (rare in
  practice — needs ~16.7M FrameSets on one connection — but an always-on server is exactly the
  profile that eventually hits it), HIGH impact when it does: the receiver's plain numeric
  `<`/`==` comparison judged every FrameSet arriving after the wrap as permanently stale, silently
  killing input processing for that connection forever. Fixed with an RFC1982-style circular
  comparison; `OutputSequence` now wraps to match what the wire (`WriteTriad`/`ReadTriad`, 24 bits)
  and the peer's ACK/NACK actually report.
- **Chunk radius change mid-game never re-sent NetworkChunkPublisherUpdate.** HIGH confidence,
  client-visible impact. `PlayerChunkTracker.PublisherCenterChanged` only compares chunk X/Z, so
  increasing render distance without crossing a chunk boundary streamed new `LevelChunkPacket`s but
  never told the client its publish radius grew — per the packet's documented purpose, the client
  culls anything outside its last-told radius even if the chunk data already arrived.
  Fixed with `PlayerChunkTracker.ForcePublisherRefresh()`, called from the chunk-radius-request
  handler.
- **Effect re-application always overwrote, ignoring vanilla's "stronger wins" rule.** HIGH
  confidence, HIGH impact. `Player.ApplyOrRefreshEffect` unconditionally replaced the active
  instance; drinking a weak Regeneration I potion while Regeneration II was still ticking silently
  downgraded/shortened it. Fixed to keep the existing instance unless the new one is a strictly
  higher amplifier, or the same amplifier with more remaining duration.

**Investigated, not fixed this pass (real gaps, larger scope — documented rather than rushed):**

- **No movement speed/teleport-distance validation** — `MovementSystem` applies client-reported
  position unconditionally, no plausibility check against a max blocks/tick. Confirmed real gap
  (anti-cheat baseline), not urgent per the session-lifecycle audit's own framing.
- ~~No saturation system at all~~ — **closed in Phase XXV**: `Player.Saturation` is real state,
  depleted before `Hunger` on each exhaustion-threshold crossing and restored (capped at the current
  hunger level) on eating. See `docs/history/phases/phase-xxv-survival-foundation-findings.md`.
- ~~Exhaustion only comes from sprinting~~ — **closed in Phase XXV** for mining and taking damage
  (each already had a single call-site funnel to add the source to). Jumping and walking's own
  smaller per-block cost are still not sourced — walking needs distance tracking `MovementSystem`
  doesn't have yet.
- ~~XP is not reset/dropped on death~~, and ~~only XP + pose persist across reconnect~~ — **both
  closed in Phase XXV**: death resets XP to zero, and `PlayerDataBlob` v3 persists
  health/hunger/saturation/exhaustion alongside pose and XP. Effects are still not persisted
  (timed, expected to lapse naturally — a smaller, deliberately deferred gap).
- **`BreakDuration`'s tool-efficiency-vs-tier-mismatch formula diverges from vanilla's** — a
  wood pickaxe on a diamond-tier block should still dig faster than bare hands (no drop, but faster
  time); Zenith resets to 1.0× speed on any tier mismatch. Currently a no-op (no registered block
  has a tier gap yet), becomes a real bug the moment one is added.

**Confirmed clean (no bug found):** chunk/palette encoding (sub-chunk version, index ordering, bit
widths); block-placement self-suffocation/obstruction checks; LevelDB overlay write queue (no
blocking `.Wait()` on the game loop, no unrecoverable loss on graceful shutdown); RakNet MTU
handshake and datagram sizing; RakNet thread-safety of the send path; RakNet duplicate-connection/
reconnect handling and session timeout; login → resource-pack → spawn packet sequence (matches the
documented handshake order exactly); duplicate/racing player join; disconnect cleanup (chest-opener
release, peer un-announce, inventory/playerdata persist, vehicle dismount) — all already correct;
NaN/Infinity position/rotation input validation; XP level curve (matches vanilla's exact piecewise
formula); well-fed regen threshold/cadence.

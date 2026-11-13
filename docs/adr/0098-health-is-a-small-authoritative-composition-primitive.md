# ADR §98 — Health is a small authoritative composition primitive

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §98`.

**Choice:** Model health as `HealthState`, composed by `Player` today and available for a later actor without introducing `Entity`, `LivingEntity`, ECS, attributes, effects, or a combat framework.

1. **State and cause are explicit.** `HealthState(maximum)` owns current health, fatal state, and the first `DamageSource`. A source currently carries `DamageCause` (`Generic`, `Fall`, `Void`, `Melee`); it has no attacker identity until a real attribution feature needs one. Invalid amounts (zero, negative, NaN, infinity) are rejected without mutation.
2. **One writer.** `Player.ApplyDamage` is internal and called by the gameplay owner. Handlers, packets, and async continuations do not set health. `DamageResult.Died` is emitted once; later requests are `AlreadyDead`, preserving the first fatal cause.
3. **Death is two deliberate steps.** Health first accepts the fatal transition, then `Player.TryFinalizeDeath` performs the one-time player lifecycle cleanup. This lets the gameplay caller perform concrete death work without making the health leaf know inventories, worlds, packets, or players.
4. **Death loot is atomic at the real boundary.** `FloorDropFanout` plans every craft-grid, bag, and cursor stack before committing any floor cell or clearing a source. Refusal keeps the entire inventory. An unexpected post-plan store failure restores the floor snapshot before it escapes. Memory state is authoritative during the session; persistence is requested only after a successful in-memory transfer.
5. **Replication remains protocol-specific.** The victim receives the existing full local HUD seed. Existing peers receive a focused `minecraft:health` `UpdateAttributes`, and `PlayerVisibility` sends health after `AddPlayer` for late viewers. `UpdateAttributesPacket.CreateHealth` is a packet helper, not an attribute framework.
6. **Respawn starts the next lifecycle.** The gameplay owner applies pose, restores `HealthState` to its configured maximum, then sends local and peer health updates before the normal inventory/UI resync.

**Why:** Fall and void already proved that direct `Player.Health` writes and a cause string were too weak: a second fatal call could perform drops before discovering death had happened, and death loot could clear only the slots that happened to fit. `HealthState` is the smallest shared domain boundary that makes damage validation, fatal idempotence, and future actor composition testable without moving fall policy out of `MovementSystem` or inventing a global `DamageSystem`.

**Non-goals:** armor; hunger; effects; generic attributes; attackers/kill credit; AI; a generic combat engine; entity hierarchy; ECS; plugin hooks. A first actor can compose `HealthState` and apply an explicit `DamageSource.Melee`; adding attacker identity or a new damage policy requires evidence from that feature.

**Verification:** leaf `HealthStateTests` covers controlled melee, invalid input, one lethal transition, and respawn reset. Gameplay tests cover fall, void, conservation across bag/cursor/craft UI, capacity refusal with no partial death loot, and peer health fan-out. The external `smoke:respawn` script currently asserts the obsolete "bag intact" policy, so it is not counted as validation for this change; replace that assertion with an observable death-loot policy and add a peer-health scenario before player-versus-actor gameplay lands.

**Status (ago 2026):** Shipped — no new `IGameSystem`; fall/void remain in `MovementSystem`, which already owns their tick ordering.

**Adendo (Phase XXIII-B, invulnerability window):** `HealthState.Apply` gained a vanilla hit-invulnerability
window (`_invulnerableUntilTick`/`_lastDamageTaken`, 10 ticks / 0.5s — Java's `hurtResistantTime`) — a
real-client-review finding that Zenith had no invulnerability concept anywhere, so simultaneous or
same-tick-re-checking damage sources could stack far beyond vanilla feel. A strictly-greater new hit
still lands (Java's "harder hit overrides" rule); Void bypasses the window entirely (falling out of
the world damages every tick, matching vanilla). This doesn't change any of the six invariants above
— still one writer, still one fatal transition, still no attacker identity — it's an additional guard
inside `Apply` itself, not a new write path. Recorded here (found via a later architecture audit that
this addition had never been folded back into this ADR's text) purely to close a doc/code drift gap.

**Adendo (ago 2026 — override applies the delta, not the full amount):** a harder hit that overrides
an active window was subtracting its *full* `amount` from `Current`, on top of whatever the hit that
opened the window had already subtracted — double-counting the overlapping portion (found via a
reference-parity audit against Dragonfly/PocketMine/vanilla, all three of which subtract only the
delta over the previous hit in this exact case: `damageLeft -= p.lastDamage`,
`MODIFIER_PREVIOUS_DAMAGE_COOLDOWN`, `actuallyHurt(source, amount - lastHurt)`). `Apply` now applies
`amount - _lastDamageTaken` when overriding an active window, and the full `amount` otherwise (window
expired, or Void's bypass). No change to the rejection rule, the window duration, or Void's bypass.

# ADR §135 — A fatal combat blow always lands; loot-capacity failure only drops the loot, never the kill

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §135`.

> **Superseded in part by §149:** `IDamageableActor`, `GroundMobCombat`, `DamageableActorCombat` and the per-species stores described here no longer exist; every species is an ECS actor on one replication path.

**Context:** found via the same reference-parity audit that produced §133/§134.
`DamageableActorCombatCore.TryApplyDamage` pre-checked `FloorDropFanout.CanDeposit` and refused the
*entire* damage application — not just the loot — whenever the incoming hit would be fatal and no
nearby floor cell could take the loot drop. `FloorDropStore.SoftCap` (`2048`) is global, not
per-area/per-chunk: once that many drop cells exist anywhere on the server, `TryPlanDeposits` refuses
every *new* cell everywhere, even in an area with zero drops nearby, until enough cells despawn
(`DefaultDespawnTicks` = 5 min) or get picked up. Combined, sustained floor-drop pressure from
anywhere on the server — a farm, lag, or just normal play at scale — could leave any mob that still
needs to drop loot permanently stuck just above 0 HP, unkillable, world-wide, until unrelated drops
elsewhere aged out. No reference server (PowerNukkitX, Dragonfly, PocketMine, vanilla) gates a kill on
loot-drop capacity at all.

**Choice:** `TryApplyDamage` now always applies `HealthState.Apply` and always proceeds through the
death transition when it's fatal. Loot deposit is attempted (`FloorDropFanout.TryDeposit`) but its
return value is no longer checked — if the floor-drop area is saturated, the loot silently does not
drop; the kill, XP, and destroy sequence proceed regardless. `FloorDropStore.SoftCap` stays global and
unchanged — this is not a capacity redesign, it decouples "can this specific loot item be placed"
from "can this creature die," which is the actual game-breaking symptom, without touching the RAM
bound the SoftCap exists for. `FloorDropStore.TryAddOrMerge` already logs once when SoftCap refuses a
cell, which remains the operational signal for the underlying pressure.

**Non-goal, deliberately left alone:** `BlockEditSystem`'s block-break call sites
(`FloorDropFanout.CanDeposit`/`CanDepositBatch`) keep their pre-check, unchanged. Mining is a
voluntary, retryable player action — refusing to break a block into a saturated floor area (rather
than silently losing the drop) is a reasonable, recoverable trade-off there. Combat is not retryable
in the same way: the attack already landed, and "you can't kill this mob right now" is not a
comparable UX to "you can't mine this block right now, try again in a bit" — it reads as the mob being
bugged/invincible, indefinitely, for a reason with no in-game signal.

**Non-goals:** a per-chunk/per-area `SoftCap`; any change to `FloorDropStore`'s capacity mechanism
itself; changing `BlockEditSystem`'s loot-capacity guard.

**Status (ago 2026):** Shipped — `DamageableActorCombatCore.cs`.

# ADR §129 — `online.FirstOrDefault(p => p.RuntimeId == id)` → `PlayerManager.GetByRuntimeId` — finishes a half-done refactor

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §129`.

**Context:** Another external-review finding, this one confirmed to already be half-built into the
codebase: `PlayerManager` has had `_byRuntimeId`/`GetByRuntimeId` (O(1)) since an earlier pass, and
its own doc comment says exactly why — "Multiple gameplay consumers already resolve a player from an
explicit wire-supplied RuntimeId... via an O(N) `online.FirstOrDefault` scan — this index makes that
resolution O(1) instead." The index was built; the call sites it was built for were never migrated.
Found and confirmed 8 of them, all in `Gameplay/Entities`: Enderman aggro target, Golem/Creeper/
Zombie/Spider retained target, Zombie/Villager attacker-for-knockback-or-provocation, Minecart
occupant resolution.

**Choice:** Swapped all 8 to `_players.GetByRuntimeId(id)` — every site already had `PlayerManager`
available via its existing constructor injection, so this is a pure O(N)→O(1) swap with identical
semantics (`_byRuntimeId` is maintained on the exact same `TryAdd`/`Remove` calls that populate
`online` itself, so the resolvable player set is always identical between the two). Two methods that
took `online` *only* for this lookup (`CreeperSystem.FindOrAcquireTarget`,
`MinecartSystem.ResolveOccupant`) had the now-redundant parameter removed — `ResolveOccupant` also
lost its `static`-incompatible dependency entirely, `FindOrAcquireTarget` went from `static` to an
instance method since it now needs `_players`.

**Not touched, found real but out of scope for this pass:**
- `PlayerPresenceAnnouncer.Broadcast` calls `PlayerManager.SnapshotOnline()` (a fresh allocation)
  instead of reusing `GameLoop`'s per-tick snapshot — confirmed real, but login/quit events may fire
  from outside the tick-scoped snapshot's valid lifetime/thread, which needs verifying before treating
  this as a safe swap. Low priority regardless — join/quit is a rare event, not a hot path.
- A larger, structurally different finding from the same review: several systems
  (`MovementSystem.ApplyDamage`, `PlayerDamage.Apply`, `BlockDigSystem`, `FloorDropSystem`) take
  `online` on functions that act on one player, purely so a later step in the same call can fan out a
  replication effect (`PlayerVisibility.RelayHealth`, `BlockCrackFanout`, etc.). Confirmed accurate by
  reading `PlayerDamage.Apply`'s body — `online` there is used exclusively for
  `RelayHealth`/`RelayHurt`/`RelayDeath`/`ChestLidFanout`/`FloorDropFanout`, never for the damage
  decision itself. Separating "decide the effect on one player" from "replicate the consequence to
  peers" is architecturally sound and matches this project's own `Gameplay decide; Protocol transmite`
  principle, but it's a genuinely larger, multi-file refactor of call-site boundaries — not something
  to fold into an opportunistic-fix pass. Left for a deliberate follow-up, not implemented here.

**Status (ago 2026):** Shipped — `EndermanSystem.cs`, `GolemSystem.cs`, `SpiderSystem.cs`,
`ZombieSystem.cs`, `CreeperSystem.cs`, `VillagerSystem.cs`, `MinecartSystem.cs`. No new tests — the
existing 1049-case suite already exercises every one of these target-resolution paths and stayed
green unchanged, which is exactly the expected signal for a semantics-preserving O(N)→O(1) swap.

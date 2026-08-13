# Phase XVI — Entity Runtime & Gameplay Depth Findings

Phase XVI added three concrete, bounded pieces of gameplay depth — Cow breeding (passive
variation), Zombie knockback (combat variation), and despawn-when-unseen across all five ground
mobs (world lifecycle) — specifically to keep pressuring `IGroundMob`/`GroundMobCombat`/
`GroundMobMovement` (Phase XIV.1/XV) and to force a first real comparison of lifecycle/storage/
replication across every entity category in the codebase, not just mobs. No player-feed breeding
trigger, no additional passive mobs (Sheep/Pig/Chicken), no crit/status-effect combat variants,
and no chunk-activation/mob-limit system were built — each objective category only needed one
concrete case to generate evidence.

## What was built

- **Breeding** (`Gameplay/Systems/CowSystem.cs`): proximity-only auto-breeding. Two cows within
  `BreedRadius` (2f), both off cooldown, produce a calf; `BreedCooldownTicks = 1200` (60s);
  `MaxPopulation = 32` is a concrete, local cap — not a general "mob limit" system. At most one
  birth per tick.
- **Knockback** (`Gameplay/Systems/ZombieSystem.cs`): a landed player melee hit pushes the zombie
  away from the attacker (`KnockbackImpulse = 0.3f`), decaying `KnockbackDecayPerTick = 0.5f` per
  tick via `GroundMobMovement.CanStandAt` validation, until negligible. Zombie-only — not lifted
  into `GroundMobCombat`, since only one mob needed it.
- **Despawn-when-unseen** (`Gameplay/GroundMobLifecycle.cs`, new): a pure function,
  `EvaluateDespawn(position, online, despawnRadius, currentTick, lastSeenNearPlayerTick,
  despawnTicks = 6000)`, wired into all five ground mob systems (Cow, Zombie, Skeleton, Creeper,
  Enderman) as `TryDespawn(...)` at the top of each system's per-mob loop.

All three shipped with tests (`GroundMobLifecycleTests.cs`, plus targeted additions to
`ZombieSystemTests.cs` and `CowSystemTests.cs`) rather than a generic test harness, per the phase
brief's instruction not to build shared test infrastructure ahead of measured duplication. Full
suite: 728/728 passing (baseline 716 + 12 new), no regressions.

## Confirmed abstractions

`GroundMobCombat` and `GroundMobMovement` (Phase XIV.1/XV) remain valid and stayed small under
this phase's pressure. Knockback and breeding are Zombie-only and Cow-only respectively — neither
needed to touch the shared helpers, which is itself evidence the original extraction was scoped
correctly: it holds exactly "damage/death/loot/XP bookkeeping" and "can this position be stood
on," nothing else. Nothing added this phase forced `IGroundMob` to grow a member.

## New pressure points

- **`GroundMobLifecycle` was justified faster than `GroundMobMovement` was.** `GroundMobMovement`
  waited for a third *walking* mob before extraction (Phase XV). `GroundMobLifecycle` was
  extracted on its first use, because the despawn rule needed to apply to all five mobs
  *simultaneously* in the same phase — the "three concrete consumers" bar was met at design time,
  not discovered incrementally. This is a variant of the same rule, not an exception to it: the
  evidence threshold didn't move, only how quickly it was reached.
- **Despawn-when-unseen is a second, independent age-based-expiry shape, not a generalization of
  the first.** `FloorDropStore.DefaultDespawnTicks = 6000` (`World/FloorDropStore.cs`) ages a
  dropped item purely on elapsed ticks regardless of player proximity. `ProjectileSystem
  .MaximumLifetimeTicks = 80` (`Gameplay/Systems/ProjectileSystem.cs`) is the same shape —
  pure age, no visibility check. `GroundMobLifecycle.EvaluateDespawn` is deliberately different:
  it resets the clock every tick a player is nearby and only expires after sustained absence. The
  `6000`-tick constant was reused from `FloorDropStore` for parity of *feel* (a dropped item and
  an unseen mob linger about as long), but the two mechanisms are not the same function and were
  not merged — a premature "DespawnTimer" abstraction spanning both would have hidden a real
  behavioral difference (visibility-reset vs. pure age) behind a shared name.
- **The ground-mob store shape is now duplicated five times**, unchanged from Phase XV's finding:
  `CowStore`/`ZombieStore`/`SkeletonStore`/`CreeperStore`/`EndermanStore` are each an ~10-line
  `List<T>` wrapper with `Active`/`TryAdd`/`Remove`. `ProjectileStore` is a sixth, structurally
  identical wrapper (with a `SoftCap`) for an entity category outside the ground-mob family. Still
  not extracted — no measured maintenance cost has appeared, and the five-mob despawn wiring this
  phase went through each store's existing shape without needing to touch it. If a sixth ground
  mob or a shared soft-cap requirement appears, this is the leading Phase XVII candidate (see
  below).
- **Entity-category lifecycle/storage/replication comparison** (required by this phase's
  checkpoint, across all real categories in the codebase, not just mobs):

  | Category | Identity/lifecycle | Despawn/expiry | Persisted to disk? | Replication |
  |---|---|---|---|---|
  | Player | Long-lived object in `PlayerManager`; `IsInGame`/`IsSpawning` gate, not a timer | Never despawns — connection-driven | Yes (`pd:{uuid}`, `inv:{uuid}`, `ar:{uuid}` in `WorldStorageKeys`/`PlayerDataBlob`) | Event-driven fan-out (`PlayerVisibility.AnnounceJoin/AnnounceLeave/RelayHealth`, etc.) |
  | Ground mob | `IGroundMob` + per-species store (`List<T>` wrapper) | `GroundMobLifecycle.EvaluateDespawn` — visibility-reset age | No — RAM only | Per-tick `ActorInterest`-gated viewer reconciliation |
  | Projectile | `ProjectileStore` (`SoftCap 2048`) | Pure age (`AgeTicks >= 80`), independent of visibility | No — RAM only | Per-tick `ActorInterest`-gated viewer reconciliation (same mechanism as mobs) |
  | Floor drop | `FloorDropStore`, cell-keyed | Pure age (`TickDespawn`, `DefaultDespawnTicks = 6000`) | No — RAM only | `FloorDropFanout.Publish`, in-range/InGame peers only |
  | Falling block | `FallingBlockStore` (`SoftCap 512`), doc'd "RAM-only — never persisted" | No timer — removed on landing | No | (landing-triggered, not visibility-polled) |

  The pattern that holds across every category except Player: nothing is disk-persisted, and
  every category uses *some* per-tick or per-event viewer reconciliation rather than a shared
  broadcast primitive — but the actual removal trigger differs by category (visibility-reset age,
  pure age, or landing event), and forcing them into one interface would erase exactly the
  distinction that matters for tuning each one independently.
- **The deferred player-feed breeding trigger remains a documented gap, not a bug.** Real
  proximity-only breeding was chosen specifically to avoid building intent/protocol plumbing that
  doesn't exist yet: `InventoryTransactionPacket.TypeItemUseOnActor` currently only routes
  `ActorAttack`, not a generic "interact with entity while holding item X" action. This is
  unrelated to the runtime/entity-model questions this phase targets and was intentionally left
  unsolved rather than expanded into out-of-scope wire work.

## Rejected abstractions

- **A generic entity/store interface spanning Player, ground mobs, Projectile, FloorDrop, and
  FallingBlock.** The comparison table above is the concrete reason: each category's despawn
  trigger is structurally different (connection state, visibility-reset age, pure age, landing
  event), and each category's persistence and replication needs differ (Player alone is
  disk-persisted; FloorDrop is cell-keyed rather than per-instance; FallingBlock has no despawn at
  all). A shared interface would need to either abstract over incompatible removal semantics or
  degrade to a marker interface with no real behavior — neither is worth the indirection for five
  concrete, already-small implementations.
- **`DespawnTimer` merging `GroundMobLifecycle`'s visibility-reset expiry with
  `FloorDropStore`/`ProjectileSystem`'s pure-age expiry.** Rejected specifically because this
  phase is what surfaced the difference: reusing the `6000`-tick constant for a *similar feel* is
  not evidence the two mechanisms are the same function.
- **Inheritance-based mob modeling (`Entity → LivingEntity → Mob → HostileMob → Zombie`,
  `BaseEntity`/`BaseMob`).** Still rejected on the same grounds as Phase XIV.1/XV: five ground
  mobs plus a sixth non-mob entity category (Projectile) all satisfy `IGroundMob`-shaped or
  store-shaped contracts through composition and shared static helpers, with zero virtual
  dispatch and zero shared base class, and this phase's three new features (breeding, knockback,
  despawn) each landed in exactly one or five call sites without requiring a new inheritance
  layer to be threaded through.
- **A generic component system or ECS.** No new evidence this phase changes the prior phases'
  conclusion — three concrete behavioral additions, each fully local to its owning system, is
  still the opposite signal from "many mobs need the same optional capability toggled on and
  off."

## Possible future Phase XVII candidates (evidence-gated only)

- If a **sixth** `List<T>`-wrapper store appears (ground mob or otherwise) and needs the same
  soft-cap behavior `ProjectileStore` already has, consider a small `ActiveActorStore<T>`
  wrapping `TryAdd`/`Remove`/`Active`/optional cap — six real, structurally identical instances
  would cross the threshold this phase's five did not.
- If a second mob needs knockback (or any other combat variant currently local to
  `ZombieSystem`), that is the trigger to consider folding it into `GroundMobCombat` — not before.
- The player-feed-breeding gap (`InventoryTransactionPacket` interact-with-entity support) is a
  legitimate, scoped future slice if breeding needs to become player-driven, but it is protocol/
  intent work, not an entity-runtime abstraction question, and should be scoped as its own phase.

## After adding more real gameplay, what concepts became stable enough to exist?

`IGroundMob`, `GroundMobCombat`, and `GroundMobMovement` — unchanged since Phase XV, and this
phase's three additions (one of which, knockback, is species-specific and deliberately did *not*
join them) is itself the strongest evidence yet that the boundary is right: features don't reach
for these helpers unless they are actually shared combat/movement bookkeeping, and features that
aren't shared (breeding, knockback) stay entirely local without friction. `GroundMobLifecycle` is
now also stable — a pure function, five real callers, one deliberately-distinct sibling
(`FloorDropStore`/`ProjectileSystem`'s age-only expiry) that proves it isn't a candidate for
further merging. What did *not* become stable, and should stay rejected until new evidence
appears: any concept broader than "ground mob" — a Player/Mob/Projectile/FloorDrop/FallingBlock
unifying interface remains unjustified because this phase's own comparison table shows their
lifecycles are genuinely different, not just differently named.

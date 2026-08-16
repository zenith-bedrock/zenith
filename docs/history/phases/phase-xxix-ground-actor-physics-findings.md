# Phase XXIX — Actor Physics, Fluid Semantics & Ground Locomotion Fidelity: findings

## 1. The original problem

`GroundMobMovement.CanStandAt` (the one shared predicate all 9 ground-navigating mob systems routed
through — Zombie, Cow, Creeper, Skeleton, Spider, Villager, Golem, Enderman's teleport-landing check,
Minecart's rolling fallback) asked exactly one physical question about the cell beneath a mob's feet:

```csharp
world.GetBlock(blockX, blockY - 1, blockZ) != World.World.AirRuntimeId
```

"Not air" and "solid enough to stand on" were treated as the same fact. Water is not air, so a
zombie or cow walking over a lake read as standing on land — the real-client symptom that opened
this phase. The same idiom (`== Air` / `!= Air` as a universal collision/support proxy) recurred
independently in five other places, all audited before any code changed:
`ProjectileSystem.HitsWorld` (block collision), `GravitySystem`'s three falling-block
support/landing checks, and `OverworldTerrainSampler.SampleSpawnFeetY` (spawn support). Ground mobs
also had **zero vertical (Y) physics** at all — every `TryMove` wrote only X/Z; Y was fixed at spawn
and never re-integrated, confirmed by reading all 9 consumers in full before designing anything.

## 2. References consulted

Local corpus at `D:\Development\bedrock`: **PocketMine-MP** (PHP), **Basalt** (C#), **Dragonfly**
(Go) — all three read directly for behavioral rules, not architecture. Cross-referenced for every
question that materially affected the design; no GitHub clone was needed, the local corpus had
sufficient evidence for every question asked.

- Ground support = solid collision-box overlap, never a liquid: PocketMine `Liquid::recalculateCollisionBoxes()`
  returns `[]` unconditionally; Basalt's `IsSolid()` explicitly excludes `Liquid`; Dragonfly derives
  `onGround` from real AABB collision resolution, which liquids never participate in.
- Living-entity gravity: **0.08 blocks/tick²**, drag **0.02** (0.98 retention), terminal fall speed
  **≈3.92 blocks/tick** — independently corroborated by PocketMine (`Living::getInitialGravity/
  getInitialDragMultiplier`) and Basalt (`EntityMovementTrait.GravityPerTick/Drag/TerminalVelocity`).
  Deliberately distinct from Zenith's pre-existing falling-block gravity (0.04, `GravitySystem`) —
  vanilla itself gives blocks and living entities different fall rates; this is not an inconsistency
  to unify.
- Falling is gradual with a per-tick landing check in every reference read — none snap-teleport.
- Step-up is a general two-cell retry (try the blocked move again one Y higher, keep it only if it
  makes more horizontal progress than not stepping), not a special case hard-coded to "exactly one
  block, air above" — PocketMine's `Entity.php` step-height retry was the clearest example.
- Step-down has no dedicated mechanism anywhere — a mob that walks off an edge just falls under
  ordinary gravity next tick, same code path as any other fall.
- Ground-mob water entry: no reference gives ordinary land mobs buoyancy or slowdown. Water simply
  isn't solid support; gravity keeps applying; the mob can still traverse the water's floor as
  ordinary terrain. Basalt's genuine buoyancy/current code is gated behind an explicit `IsSwimming`
  flag that generic land mobs never carry — full swimming is real extra machinery, not an emergent
  side effect of "water isn't support."
- Projectiles vs. fluid: PocketMine's and Dragonfly's arrow/projectile collision reuses the same
  BBox machinery as everything else, and since liquids contribute no BBox, arrows structurally pass
  through water in both references. No special-cased water splash logic exists in either codebase.
- Partial-height blocks (slabs, water depth/level): confirmed genuinely out of scope for a minimal
  implementation — none of the three references treat it as a separate mechanism from ordinary AABB
  collision; it would require per-block custom collision shapes, a materially larger feature.

## 3. Physical block semantics introduced

`Blocks.cs` had no flags/properties concept before this phase — only three ad hoc `HashSet<int>`
predicates (`IsPlaceable`, `IsGravity`, `IsChest`). A fourth, `_fluidIds` (seeded with `Water` only —
Zenith has no lava block yet), was added the same way, and five new methods compose on top of it:

```
IsAir(rid)                 — literal empty air
IsFluid(rid)                — water (and any future fluid)
BlocksMovement(rid)         — real solid collision: !IsAir && !IsFluid
CanSupportGroundActor(rid)  — same fact as BlocksMovement today, named separately
CanOccupy(rid)               — !BlocksMovement(rid) — air or fluid, a body may be there
```

`CanSupportGroundActor` and `BlocksMovement` reduce to the identical `IsSolid` check today — kept as
two named call shapes (not one shared predicate call) because a future consumer (a partial-height
block, say) may legitimately need to diverge, per the phase brief's explicit "if two consumers
genuinely share the same rule, reuse it; if they differ, keep distinct predicates" — today they don't
differ, so both call the same private `IsSolid`, but the name at each call site documents which
physical question that caller is actually asking.

No `BlockBehavior` inheritance tree, no reflection-driven registry, no material framework — five
static methods over one `HashSet<int>`, the same shape as the three predicates already there.

## 4. Locomotion model

`GroundMobMovement.cs` (previously a single 22-line file with one method) now exposes three static,
stateless methods — still a pure resolver over primitives, no knowledge of "what a mob is":

- **`TryMoveHorizontal(world, x, y, z, desiredX, desiredZ, out resolvedY)`** — AI's chosen
  destination, resolved against occupancy only (no support requirement — a mob may walk off a ledge;
  whether it then falls is the next tick's problem, not this call's). Tries the destination at the
  current Y first (flat ground, descending terrain — the "step down" fix falls straight out of
  removing the old support gate), then one block higher (general step-up retry, not a special case).
- **`ResolveVertical(world, x, z, ref y, ref fallSpeed, out grounded)`** — one tick of gravity:
  accelerates while unsupported, halts and lands the instant the fall crosses a supporting block's
  top face. Gradual, per-tick, matching every reference read.
- **`IsSupportedGroundCell(world, x, y, z)`** — the narrow case: movers with no vertical model of
  their own (Minecart's velocity-rolling fallback, Enderman's teleport-landing validation). Same
  shape as the old `CanStandAt`, water-support bug fixed, nothing else changed — these two movers
  don't walk tick-by-tick or fall, so the richer resolver would be unearned complexity for them.

### Fall-speed state: no new component

`Velocity` (`Ecs/Components.cs`) already existed as ECS-shared data with per-system-owned behavior —
Zombie's knockback and Minecart's rolling motion already used `X`/`Z`. Phase XXIX reuses `Velocity.Y`
as every ECS ground mob's downward fall-speed magnitude (same sign convention `GravitySystem`'s
`FallingBlockEntry.VelocityY` already used) instead of adding a new component. Cow, Skeleton, and
Spider — ECS mobs that never attached `Velocity` before, since nothing read/wrote `X`/`Z` for them —
now attach it at spawn, for `.Y` alone. The legacy roster (Villager, Golem, Creeper — plain classes,
no ECS store) gained one new field each, `VerticalFallSpeed`, the direct equivalent.

### Migrated per mob

| Mob | Roster | Gets full resolver (occupancy + step-up + gravity) |
|---|---|---|
| Zombie, Cow, Skeleton, Spider | ECS | Yes |
| Villager, Golem, Creeper | Legacy | Yes |
| Minecart | ECS | No — `IsSupportedGroundCell` only (rail-adjacent rolling fallback, no vertical model, out of this phase's scope) |
| Enderman | Legacy | No — `IsSupportedGroundCell` only (teleport-only movement, no continuous walking) |

Deliberate scope line, not an oversight: Minecart and Enderman never had a vertical model before this
phase and don't walk continuously — adding gravity/step-up to a rail vehicle or a teleporting mob
would be exactly the kind of speculative buildout the brief explicitly warned against. Both still got
the water-as-support bug fix, since the DoD requires no consumer relying on non-air as a universal
support proxy.

## 5. Gravity model / constants

- Gravity: **0.08 blocks/tick²**
- Drag: **0.98 retention/tick**
- Terminal fall speed: **3.92 blocks/tick**

All three sourced from §2, applied identically to every ECS and legacy ground mob. Landing is a
bounded downward cell scan (at most ⌈terminal fall speed⌉ ≈ 4 iterations per tick, only entered when
the fall actually crosses a block boundary that tick) — no generalized AABB collision object, per the
brief's "prefer bounded block probes."

## 6. Fluid handling

Water is `IsFluid`, never `CanSupportGroundActor`. Ground-actor occupancy (`TryMoveHorizontal`'s
`IsClear`) treats fluid the same as air — a mob's feet/head cells may be water, so a mob can walk
into/through water exactly as reference behavior describes: not blocked, not slowed, no buoyancy;
gravity keeps pulling it toward the water's floor. **Full swimming/buoyancy is deliberately not
implemented** — this is the documented simplification the brief explicitly permits ("acceptable if
full swimming/buoyancy remains deferred"). `FishSystem` is completely untouched: it never called
`GroundMobMovement`, and two regression tests (`Fish_water_behavior_is_not_broken`,
`Bat_flight_is_not_affected_by_ground_gravity`) prove neither non-ground movement mode was affected.

## 7. Other consumers reviewed against the new semantics

- **`ProjectileSystem.HitsWorld`**: changed from `!= Air` to `Blocks.BlocksMovement(...)` — arrows now
  pass through water instead of splashing on it. A deliberate behavior change, not an oversight: both
  PocketMine and Dragonfly give liquids no projectile collision box, and the old code used the
  identical buggy idiom this whole phase exists to close. Documented here per the brief's explicit
  "do not automatically change behavior — ask the right question and document the decision."
- **`GravitySystem`** (falling-block support/landing, sand/gravel): all three `!= Air`/`== Air`
  support checks changed to `Blocks.BlocksMovement(...)`. Sand/gravel dropped into water now keeps
  sinking instead of resting on the water's surface — same bug class, same fix, falling-block gravity
  constants (0.04/0.98, PowerNukkitX-sourced) intentionally left untouched.
- **Spawn safety** (`OverworldTerrainSampler.SampleSpawnFeetY`): the support clause changed from
  `!= Blocks.Air` to `Blocks.CanSupportGroundActor(...)` — a spawn column can no longer resolve to
  standing on water.
- **`FishSystem`/`BatSystem`**: audited, confirmed to have their own bespoke movement-validity rules
  that never call `GroundMobMovement`, left untouched. Proven by regression test, not just inspection.

## 8. Tick ordering

No reordering was needed. Every consumer reads `World.GetBlock` live with no per-tick caching or
snapshot dependency, so a horizontal/vertical resolve call takes effect wherever it's placed in an
already-registered system's `Tick`. `PlayerSpatialIndexSystem` (Phase XXVIII) still runs immediately
after `MovementSystem`; ground mob systems still run in their existing relative order; the new
`ApplyGravity` call sits at the end of each mob's per-actor tick body, after AI/attack/movement
resolution and before viewer reconciliation, so replication always observes the final resolved pose
for that tick.

## 9. Benchmark results

Measured with a throwaway Release-mode Stopwatch harness (`ZombieSystem.Tick` end-to-end, not just
the locomotion calls in isolation — the honest cost includes despawn/attack/replication bookkeeping
the resolver sits inside of), written, run, and deleted per this session's established measurement
convention — not part of the permanent suite:

| Actors | Avg tick | p95 | p99 | Alloc/tick | Alloc/actor/tick |
|---|---|---|---|---|---|
| 100 | 0.061 ms | 0.026 ms | 0.039 ms | 50.8 KB | 508 B |
| 1,000 | 1.757 ms | 2.366 ms | 4.287 ms | 761 KB | 761 B |
| 5,000 | 2.276 ms | 6.030 ms | 13.110 ms | 2.71 MB | 542 B |

Well inside a 50 ms/tick (20 TPS) budget at every measured population, including p99 at 5,000 actors.
The locomotion calls themselves (`TryMoveHorizontal`/`ResolveVertical`) are allocation-free — bounded
`GetBlock` probes only, no LINQ, no per-actor temporary collections — so the allocation figures above
are `ZombieSystem.Tick`'s existing bookkeeping (replication dictionaries, `IsKnownAliveZombieId`
scans, etc.), unrelated to this phase's additions and not a regression from them. Approximate world
block queries per actor per tick: 2 (flat-ground success) to 6 (blocked + step-up retry) for
`TryMoveHorizontal`, plus 1 (grounded, common case) to ~5 (mid-fall, landing-boundary tick) for
`ResolveVertical`.

## 10. Real-client validation checklist (not run — no live client available this session)

- [ ] Zombie/Cow walk normally on flat terrain (no regression)
- [ ] Zombie/Cow approaching a lake stop treating the water surface as ground
- [ ] Walking into shallow/deep water sinks/falls instead of floating
- [ ] Walking off a ledge falls instead of hovering in place
- [ ] Descending one-block terrain steps down instead of refusing to move
- [ ] Walking into a one-block-tall obstacle steps up onto it
- [ ] A two-block (or taller) wall still fully blocks movement
- [ ] Movement remains smooth across chunk boundaries while falling/stepping
- [ ] Fish still swim normally, confined to water
- [ ] Bats still fly freely, unaffected by ground gravity
- [ ] Mob replication (`MoveActorAbsolute`) stays smooth during a fall, no visible teleport/snap

## 11. Explicitly out of scope (deferred, not attempted)

Pathfinding/navigation meshes, jump planning, door/ladder/spider-climb navigation, entity-entity
collision, water currents, full buoyancy, lava simulation (no lava block exists yet), slab/stair/fence
partial-height collision, a full collision-shape engine, fall damage changes, and a generic
`Rigidbody`/`PhysicsComponent` framework — all named explicitly in the brief as out of scope, none
touched.

## 12. Remaining gaps / next candidates

- `DespawnLifecycle.EvaluateDespawn` and mob targeting (`SkeletonSystem.FindTarget`,
  `Zombie`/`Spider`/`Creeper` `FindOrAcquireTarget`, `GroundMobCombat`/`DamageableActorCombat`
  `ApplyPlayerMeleeAttacks`) remain O(mobs × players) proximity scans — flagged by Phase XXVIII,
  unrelated to physics, not addressed here.
- Minecart and Enderman still have no vertical physics — acceptable per §4's scope line, but a future
  phase adding real rail physics or enderman fall-damage parity would need to revisit them.
- Full swimming/buoyancy, partial-height collision, and lava remain genuinely unimplemented, not
  merely deferred by omission — see §11.
- A pre-existing, unrelated bug was found incidentally while testing spawn safety:
  `NoiseTerrainProvider.SampleBaseBlock` can return a non-palette (garbage) runtime id at some far
  noise-terrain coordinates for certain seeds. Flagged as a separate background task, not fixed here
  — it is a worldgen data-integrity bug, not a physics-semantics bug, and predates this phase (the
  old `!= Air` check would have treated the same garbage value as "solid" too).

## Definition of Done

- [x] Water no longer treated as solid support for ordinary ground mobs
- [x] Physical checks no longer rely on non-Air as a universal collision/support proxy (ground mobs,
      projectiles, falling blocks, spawn safety — all six identified consumers)
- [x] Ordinary ground mobs obey support loss and gravity
- [x] Simple step-up/step-down and obstacle interaction are coherent
- [x] Movement remains deterministic under the GameLoop (no reordering needed, no new threading)
- [x] Fish/Bat/projectile/falling-block distinctions remain intact (proven by regression test)
- [x] No ECS migration was forced (legacy roster kept its own classes, gained one field each)
- [x] No generic physics engine was introduced (three static methods, one new HashSet)
- [x] Reference behavior checked against `D:\Development\bedrock` (§2)
- [x] Benchmarks show acceptable 20 TPS cost (§9)
- [x] Regression tests pass (1019/1019 `zenith.Tests`, up from 1006 baseline)
- [x] Remaining simplifications explicitly documented (§6, §11, §12)

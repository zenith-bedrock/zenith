# Phase D — actor runtime pressure findings

**Baseline:** local Phase-D closure worktree. Measured 2026-08-11 on Windows 10.0.26200 x64, .NET SDK 10.0.302 / runtime 10.0.10. CPU model and retained-process memory were unavailable from this sandbox and are deliberately not inferred.

**Purpose:** characterize Zenith's direct, single-writer actor model before any entity-runtime or ECS experiment. This is evidence, not acceptance of ECS, a `VisibilitySystem`, a job system, or a generic actor abstraction.

## Phase C closure evidence

The concrete Zombie remains the first long-lived actor slice: `Zombie` composes `HealthState`; `ZombieSystem` owns bootstrap spawn, targeting, movement, melee, damage and idempotent removal; `EntityProtocol` projects Bedrock add/move/health/remove. `ZombieSystemTests` covers the unit lifecycle, and the separate two-client `smoke:zombie` had already observed the same actor id, movement, health changes and one removal. Phase C is not reopened here.

## Projectile vertical slice evidence

The second actor is a concrete `minecraft:snowball`, selected because it is short-lived, velocity-heavy, carries an owner id and ends by collision or lifetime rather than health/death. The bounded handler-to-gameplay handoff is `Player.SubmitProjectileIntent`; `ProjectileSystem` alone creates, advances, collides, damages and removes it on the game tick. `DamageSource` gained only the evidenced `Projectile(ownerRuntimeId)` attribution case; it is not a combat-source framework.

`ProjectileSystemTests` covers tick-owned spawn/owner retention, projectile→Zombie fatal attribution and one removal, world-impact isolation, and late-join catch-up. The previously-run two-client `smoke:projectile` observed a shared runtime id, movement, and exactly one removal for both clients. The smoke remains an external-process prerequisite for final validation; it is not folded into this C# repository.

## Reproducible actor workload

Commands, using production `GameLoop` ordering, real `ProjectileSystem`, `ProjectileStore`, protocol encoders and recording RakNet transport:

```bash
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --actors 100,1000 --actor-players 10 --ticks 200
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --players 1 --actors 100,1000 --actor-players 1 --ticks 200
```

The actor starts at Y=100 and has an 80-tick maximum lifetime. `actor-churn` seeds the target population, removes it through that real lifetime path, then replenishes on the next tick. Thus 200 ticks produce two full remove/replenish cycles: the recorded 200/2,000 removals are evidence of churn, not a static seeded population.

| Actors / observers | avg | p50 | p95 | p99 | max | alloc/tick | GC (0/1/2) | active/target | spawn fan-out | move fan-out | removes | datagrams | bytes |
|---|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---:|---:|
| 100 / 1 | 0.508 ms | 0.419 ms | 0.446 ms | 2.717 ms | 9.750 ms | 122,788 B | 2/0/0 | 100/100 | 300 | 19,800 | 200 | 619 | 689,033 |
| 1,000 / 1 | 5.215 ms | 4.497 ms | 7.736 ms | 15.738 ms | 24.066 ms | 1,218,188 B | 31/7/2 | 1,000/1,000 | 3,000 | 198,000 | 2,000 | 5,177 | 6,884,865 |
| 100 / 10 | 3.430 ms | 2.741 ms | 7.092 ms | 10.192 ms | 16.681 ms | 1,142,842 B | 27/4/0 | 100/100 | 3,000 | 198,000 | 200 | 6,190 | 6,893,030 |
| 1,000 / 10 | 48.035 ms | 45.823 ms | 60.556 ms | 76.327 ms | 130.613 ms | 11,377,498 B | 271/51/0 | 1,000/1,000 | 30,000 | 1,980,000 | 2,000 | 51,780 | 68,851,390 |

Zenith targets **20 TPS = 50 ms/tick**, not 20 ms. At 1,000 actors/10 observers the mean is only inside that budget; p95, p99 and max are not. The one-observer 1,000-actor run is 5.215 ms average and 6.9 MB egress, while ten observers is 48.035 ms and 68.9 MB. This is strong evidence that global actor replication fan-out dominates this workload. It is not a pure simulation benchmark: it intentionally combines simulation and wire work. It does not prove ECS would improve the tail; it proves Phase E must measure direct simulation separately from observer replication and must not claim ECS solves interest.

### Cost decomposition — evidence, not a profiler claim

The controlled one-versus-ten-observer comparison holds actors, ticks, projectile lifetime and all
gameplay systems constant. At 1,000 actors it adds 42.820 ms to mean tick time, 10,159,310 B/tick,
1,782,000 move fan-outs and 61,966,525 output bytes when changing only from one to ten observers.
That makes **replication projection, observer fan-out, packet encoding and transport work** the
predominant *incremental* cost. The one-observer result still contains actor iteration, collision,
lifecycle churn, one observer's projection and transport, so 5.215 ms / 1.22 MB/tick is an upper
bound for direct simulation+lifecycle rather than an isolated simulation number. A no-observer
case is intentionally not fabricated: the authoritative owner is an in-game player and the
harness is measuring a real actor path.

| Concern | Current evidence | Phase-E interpretation |
|---|---|---|
| Simulation / actor iteration | 1,000 actors/one observer has p95 7.736 ms and active count remains 1,000 | candidate for direct-vs-SoA iteration comparison |
| Lifecycle / churn | 2,000 real lifetime removals and subsequent replenishment in 200 ticks | candidate for create/remove/structural-change comparison |
| Collision / cross-actor lookup | each Projectile scans the concrete active Zombie list; the workload has no Zombies, so its cost is not isolated here | include a Zombie/Projectile collision mix; do not claim a spatial-query result yet |
| Allocation / temporary collections | `ProjectileSystem.Active.ToArray()` copies active references every tick; `RemoveWhere` performs `Active.Any` and `online.Any` for viewer entries; `SendMoveActorAbsoluteRaw` allocates a packet per actor/viewer | evidenced direct-model hot-path candidates; characterize before optimizing/pooling |
| Replication projection | every active Projectile sends a fresh move packet to every known viewer; actor-specific add methods build packet models in `EntityProtocol` | separate projection cost from simulation in the spike; keep gameplay packet-free |
| Observer fan-out | 1,000 × 10 has 1.98 M moves and 68.9 MB egress; one observer has one tenth of both | Visibility/interest concern, separate from ECS |
| Wire / transport | recording RakNet counts actual encoded outgoing datagrams/bytes; it does not copy/send to a network socket | wire encoding is included; real UDP/kernel/network saturation is not measured |

The `ToArray`/LINQ and packet-allocation observations are code evidence, not a claim that any one
of them explains a measured percentage. No pooling, list reuse, spatial index or generic runtime is
added in Phase D because doing so would corrupt the direct-model control.

## Shared and actor-specific patterns

| Concern | FallingBlock | FloorDrop | Zombie | Projectile |
|---|---|---|---|---|
| Runtime identity | SHARED entity/runtime ids | SIMILAR item-actor runtime id | SHARED entity/runtime ids | SHARED entity/runtime ids + owner |
| Position/rotation | SIMILAR continuous Y | ACTOR-SPECIFIC cell position | SIMILAR continuous XZ + yaw | SHARED continuous XYZ; no rotation yet |
| Velocity/gravity | SIMILAR vertical gravity | ACTOR-SPECIFIC none | ACTOR-SPECIFIC chase step | SHARED velocity + gravity |
| Health/damage | ACTOR-SPECIFIC none | ACTOR-SPECIFIC none | SHARED `HealthState` / damageable | ACTOR-SPECIFIC source of damage |
| Age/lifetime/active | SHARED settle/void lifecycle | SHARED delay/age/despawn | SHARED active/remove | SHARED age/impact/remove |
| Tick owner | SHARED `GameLoop` system | SHARED `GameLoop` system | SHARED `GameLoop` system | SHARED `GameLoop` system |
| World interaction | ACTOR-SPECIFIC block landing | ACTOR-SPECIFIC pickup/merge | ACTOR-SPECIFIC proximity attack | ACTOR-SPECIFIC collision |
| Projection | SIMILAR add/move/remove | SIMILAR add/remove/take | SIMILAR add/move/health/remove | SIMILAR add/move/remove |
| Late join / visibility | WIRE-SPECIFIC known chunk | WIRE-SPECIFIC known chunk | UNKNOWN global viewer set | UNKNOWN global viewer set |
| Storage | ACTOR-SPECIFIC RAM list | ACTOR-SPECIFIC sparse, mergeable cells | ACTOR-SPECIFIC RAM list | ACTOR-SPECIFIC RAM list |

`FallingBlockStore`, `ZombieStore` and `ProjectileStore` now repeat active-list ownership, identity, position/lifetime mutation, tick-local remove, add/remove projection and RAM-only lifecycle. `FloorDropStore` is deliberately only similar: its sparse cell/merge/pickup semantics make it useful evidence, not proof that it belongs in the same storage layout. This is **actor runtime pressure**, not authorization to create `ActorStore` now.

## Queries and cross-actor coupling

Current call sites establish these real access patterns: tick all active falls/zombies/projectiles; tick all floor-drop cells; find the nearest player for a Zombie; test each Projectile against active Zombies; evaluate floor-drop pickup against online players; and replicate active actors to viewers. Thus moving actors and actors requiring replication are real queries today; nearby damageable actors are a concrete Zombie-only scan; actors inside observer interest are a future requirement, not an implemented query. There is no current `all damageable actors`, radius-actor query, or generic collision query.

`ProjectileSystem → ZombieSystem → ZombieStore` is acceptable concrete vertical-slice coupling: it is one known target and preserves `ZombieSystem` as the owner of Zombie damage/removal. It is also the first growing-pressure signal: Projectile→Player+Zombie+another damageable type would require knowing multiple stores/types. That is a Phase-E question, not a reason to introduce `IDamageable`, an entity hierarchy or a query API in Phase D.

## Replication and visibility requirements

Zombie and Projectile each keep a `(actorId, playerId)` replicated set, use global online viewers, perform late-join catch-up by revisiting active actors, and fan out `RemoveActor`. Falling blocks and drops instead use known-chunk-aware paths; their packet field shapes remain wire-specific. The duplication splits into gameplay lifecycle (per-type active state/tick/remove), replication bookkeeping (viewer state and add/remove fan-out), and unavoidable `AddActor`/item-wire projection differences. `EntityProtocol` remains the wire projector.

Today Zombie/Projectile spawn, move and removal go to every in-game, alive player. They do not use actor chunk/observer state. Entering an actor area has no spatial catch-up semantics; late join is caught up globally. Leaving an area does not despawn actors; ticking does not depend on observers. An actor can be replicated before an observer knows its world chunk. These requirements must be decided independently: simulation ≠ visibility/interest ≠ replication; ECS ≠ VisibilitySystem ≠ JobSystem. The 10× observer workload demonstrates global fan-out cost but does not prescribe its replacement.

## DX baseline and deliberate deferrals

The first concrete actor needed state/store, system, composition-root registration, runtime-id allocation, protocol projection, replication bookkeeping, tests and an external smoke. Projectile additionally needed a bounded input intent, source attribution, collision and a second protocol projection method. These are legitimate DOMAIN and WIRE work. Repeated actor plumbing is the per-type active store, registration, identity allocation, viewer set, late-join scan and add/move/remove fan-out; a developer can omit one and get a partially working actor. The happy path is readable because types are concrete and no actor requires packet knowledge outside `Protocol`, but a third dynamic actor would copy meaningful scaffolding.

No source generator, registry, hierarchy, `ActorStore`, component store, ECS, VisibilitySystem, parallel simulation, public entity/plugin API or command change is introduced to improve these numbers. The direct model remains the Phase-E control.

## ECS readiness and decision gate

| Requirement | Status | Evidence / boundary |
|---|---|---|
| Runtime baseline reproducible | DONE | commands and results above |
| Health/damage/death proven | DONE for current scope | `HealthState`, death atomicity tests; breadth intentionally limited |
| Mob and different second actor | DONE | Zombie plus Snowball lifecycle/collision/owner |
| IDs + spawn/move/remove + multiple observers | DONE | focused tests and prior two-client smokes |
| Workload measures simulation and fan-out | DONE | real churn at one and ten observers, egress/fan-out recorded |
| Visibility requirements documented | DONE | global and known-chunk semantics above |
| Two features show repeated state/query/lifecycle pressure | DONE | FallingBlock, Zombie, Projectile; FloorDrop qualified supporting evidence |
| No public Entity/plugin API frozen | DONE | no such API exists |
| Objective Phase-E benchmark question | DONE | stated below |

**Gate result: READY FOR ECS SPIKE.** This authorizes an experiment only, not an ECS decision. The Phase-E ADR/spike question is: *compare the current direct world-actor model with an internal, single-writer archetype/SoA prototype using the exact identity, position/velocity, age/lifetime, spawn/remove churn, Zombie damage-targeting and 1/10-observer replication workloads established by FallingBlock, FloorDrop, Zombie and Projectile.* Players, sessions, RakNet, Packets, Protocol, inventory, containers, persistence, commands and plugins remain out of scope by default.

### What the spike is and is not trying to solve

**Evidence for investigating ECS:** three dynamic RAM-list paths now repeat lifecycle state and
tick/remove plumbing; short-lived Projectile adds a materially different access/churn pattern; the
current direct path has identified iteration/allocation candidates; concrete cross-type targeting
will grow as damageable actor types grow.

**Evidence against or not addressed by ECS:** the measured tail is predominantly the incremental
observer/projection/wire cost; ECS does not define interest policy, chunk knowledge, packet shapes
or transport batching; collision mix and retained-memory/CPU measurements are not yet isolated.

**Evidence for future visibility/interest work:** Zombie/Projectile use global viewers while
FallingBlock/FloorDrop have known-chunk-aware paths; no actor-area enter/leave semantics exist; ten
observers multiply moves, bytes and allocations by roughly ten.

**Still missing:** a mixed workload with Zombies, Projectiles, FallingBlocks and FloorDrops;
isolated CPU/retained-memory profiling; an agreed actor-interest policy; and the actual internal
archetype/SoA comparison. The falsifiable Phase-E question is: *Can an internal archetype/SoA
world-actor runtime materially reduce the measured simulation/lifecycle/allocation cost and
repeated actor plumbing of the current direct model, under the same real
Zombie/Projectile/FallingBlock/FloorDrop workloads, without degrading DX — while treating
visibility/replication as a separate concern?*

# Zenith maturity and ECS-readiness audit

**Baseline:** `6ea73bdcd77ca3e50d05b13a9322df4d53b7ace6` (`develop`, 2026-08-11).

**Scope:** source, leaf libraries, tests, benchmarks, CI, documentation, recent history, local reference clones in `D:\Development\bedrock`, and smoke-tooling references. The initial baseline was revalidated against `origin/develop` at `8a9bbb32d46cd7120e502744b728a2f6dfde43db` (2026-08-11), which committed the Runtime Validation baseline; classification below reflects that final revalidation.

## Executive summary

Zenith is a **functional/characterized multiplayer alpha**, not a mature Bedrock server. It has a real authoritative spine: clients join, see peers, move, chat, edit terrain, use inventory/chests/crafting, persist selected state, die/respawn, and observe floor-item and falling-block actors. The strongest maturity is the enforced split: gameplay decides on the single-writer tick, Protocol transmits, and Packets serialize.

Zenith is **feature-limited first and validation-limited second; it is not currently architecture-limited by missing ECS**. The immediate missing capabilities are a general health/damage contract, a real actor lifecycle/replication contract, two materially different dynamic actor behaviours, and measured actor/observer workloads. The current GameLoop is a strong basis for a future single-thread ECS, but does not establish a need for one.

ECS is **NOT READY**. At revalidation, the player runtime harness and falling-block lifecycle are complete prerequisites, but floor drops remain sparse cells with item wire. There is no mob, projectile, shared actor lifecycle, actor visibility policy, or actor workload baseline. Evaluate ECS after real actor pressure appears and before an inheritance tree or public entity/plugin API freezes the wrong shape.

## Evidence and documentation authority

| Authority | Canonical use |
|---|---|
| `ARCHITECTURE.md` | layer, ownership, runtime and freeze invariants |
| committed source + tests + smoke evidence | current implementation status |
| `docs/roadmap.md` | current sequence, gates, NOW/NEXT/LATER |
| `docs/decisions.md` | ADR history and rationale |
| this audit | point-in-time maturity and ECS evidence |

| Topic | Classification | Evidence / disposition |
|---|---|---|
| Layer split and single writer | DOCUMENTED AND IMPLEMENTED | `ARCHITECTURE.md`, `GameLoop.cs`, `ArchitectureBoundaryTests.cs` |
| LAN multiplayer/inventory/chest persistence | DOCUMENTED AND IMPLEMENTED | `alpha-gate.md`, smoke rows, intent and duplication tests |
| Falling block actor | DOCUMENTED AND IMPLEMENTED | ADR §95, `GravitySystem.cs`, `GravityTests.cs` |
| Health/damage | DOCUMENTED BUT PARTIAL | ADR §96; only fall/void damage; fall lacks recorded live smoke |
| Floor item actor | DOCUMENTED BUT PARTIAL | `FloorDropStore.cs`: sparse cell/TTL/pickup, not general free actor lifecycle |
| Runtime load harness | DOCUMENTED AND IMPLEMENTED | `RuntimeLoadHarness.cs`, `docs/runtime-validation.md`, `GameLoopTests.cs`, commit `8a9bbb3`; it remains a synthetic player baseline, not actor-scale proof |
| “~23 Cereal debts remain” | OBSOLETE DOCUMENTATION | roadmap and ADR §§88–93 record closure; technical reference must be corrected |
| H1/Yes-next as active future | OBSOLETE DOCUMENTATION | H1 is closed; feature list lacks actor/ECS dependency gates |
| ECS, generic WorldEntity, VisibilitySystem frozen | DOCUMENTED AND IMPLEMENTED AS CONSTRAINT | architecture + ADR §97; no generic layer exists |

## Current capability map

States: **Absent**, **Prototype**, **Functional**, **Characterized**, **Hardened**, **Scale-tested**, **Production-proven**. Hardened means concrete invariants/failure paths are tested, not public-production maturity.

| Capability | Current capability | Known limitation / next capability | Evidence | Maturity |
|---|---|---|---|---|
| Networking/session | owned RakNet, session SM, ordered channels, shutdown | hostile-network operations/telemetry | `src/raknet`, lifetime tests, ADR §94 | Characterized |
| Protocol | 2169 critical wire paths, generator, smoke | no complete protocol conformance/live matrix | packet tests, ADR §§79–94 | Functional |
| Runtime ownership | ordered single-writer 20 TPS GameLoop, intents | no actor budgets/telemetry | `GameLoop.cs`, boundary/intent tests | Hardened |
| World | terrain, overlays, Dimension seam, sand/gravel gravity | fluids/redstone/block breadth/multiworld | `World/`, terrain/gravity tests | Functional |
| Movement | authoritative player movement/fall/void | no full collision/anti-cheat proof | `MovementSystem.cs`, fall tests | Functional |
| Health/damage | health, fall/void death and respawn | no generic sources, mitigation, effects, knockback | `Player.cs`, ADR §96 | Prototype |
| Inventory | ISR, craft/chests, persistence and conservation | broader item semantics/actor interactions | inventory tests | Hardened |
| Persistence | Zenith LevelDB overlays/inventory/chest/player data | in-flight actors RAM-only, no actor persistence contract | storage tests | Functional |
| Player replication | join/leave, dirty pose, skin/equipment | all-online fan-out; no interest policy | `PlayerVisibility.cs`, movement tests | Functional |
| Non-player replication | item/falling-block packets | bespoke paths, no shared lifecycle/observer contract | `EntityProtocol.cs`, `ColumnSend.cs` | Prototype |
| Visibility/interest | player visibility + known-chunk checks | no spatial actor interest/activation policy | `PlayerChunkTracker.cs`, `FloorDropFanout.cs` | Prototype |
| Dynamic actors | falling blocks + floor drops | mob/projectile/general lifecycle absent | gravity/drop sources | Prototype |
| AI/navigation | none | first simple actor before pathfinding | no mob/AI source | Absent |
| Load/observability | hot-path BenchmarkDotNet; reproducible synthetic player harness | actor workloads, real-client scale and CPU/memory profiles | `RuntimeLoadHarness.cs`, `runtime-validation.md`, robustness §54 | Functional |
| Testing | leaf/unit suite, architecture tests, human smoke | Bedrock E2E not in CI | CI, `alpha-gate.md` | Characterized |
| Operations | Docker/compose/config/graceful flush | backup/recovery/SLOs/public hardening | deploy docs | Prototype |
| Commands / operator UX | one focused `/gamemode` path | next is a protocol-independent command core with a thin Bedrock metadata/autocomplete adapter; no plugin surface | ADR §52 / §99 | Prototype |
| Extensibility | internal EventBus with one consumer | no public plugin API; correct intentional freeze | ADR §78 | Prototype |

## Mature server comparison

Local source snapshots: PocketMine `6a7cc02e` (2026-07-09), PowerNukkitX `1a73a6792` (2026-08-10), Dragonfly `1c821161` (2026-08-09). They are problem catalogues, not implementation templates.

| Concern | PocketMine-MP | PowerNukkitX / Nukkit family | Dragonfly | Zenith lesson |
|---|---|---|---|---|
| Representation | broad `Entity` base hierarchy/factory/subclasses | broad `Entity extends Location`, creature hierarchy | `EntityHandle`/`EntityData`, `Ent` delegates per-type `Behaviour` | identity/lifecycle/wire concerns are universal; representation is not |
| Tick | `onUpdate` + scheduled update | `onUpdate` + scheduled update | ticks entities in visible/ticking chunks | activation is a separate future concern |
| Visibility | spawn/despawn to players tied to used chunks | spawn/despawn/viewer tracking | chunk viewers, show/hide on crossings, `Tx.Viewers` | interest policy is required when actor fan-out grows |
| AI | deliberately not vanilla-mob complete | behavior groups/sensors/controllers/memory/A* | behavior-specific movement/projectile/item logic | AI scheduling/search does not imply ECS |
| Extensibility | mature plugin API | custom entities/events | library interfaces | do not freeze Zenith’s entity API before its internal model is known |
| Cost visible in source | hierarchy/API compatibility burden | very large Entity + substantial AI machinery | still has synchronized world/chunk-viewer machinery | do not copy an inheritance tree or a mini-ECS |

Evidence: PMMP `src/entity/Entity.php` exposes `onUpdate`, `scheduleUpdate`, `spawnTo`, `despawnFrom`; PowerNukkitX `org/powernukkitx/entity/Entity.java` does likewise and has behavior/route subtrees; Dragonfly `server/world/{entity,world,tick}.go` holds entities by chunk and viewers, while `server/entity/ent.go` delegates to `Behaviour.Tick`.

### Capability maturity matrix

| Capability | Zenith | PMMP | PNX/Nukkit | Dragonfly | Evidence / gap | ECS prerequisite? |
|---|---|---|---|---|---|---|
| Runtime ownership | Hardened | Production-proven | Production-proven | Production-proven | single writer + intent tests | Foundation done |
| Network/session | Characterized | Production-proven | Production-proven | Production-proven | owned transport/NACK fix | No |
| Player replication | Functional | Production-proven | Production-proven | Production-proven | global fan-out | No |
| Inventory | Hardened | Hardened | Hardened | Functional/Hardened | conservation tests | No |
| Health/damage | Prototype | Hardened | Hardened | Functional/Hardened | fall/void only | Yes |
| Actor identity/lifecycle | Prototype | Hardened | Hardened | Hardened | per-feature stores/IDs | Yes |
| Actor replication | Prototype | Hardened | Hardened | Hardened | bespoke paths | Yes |
| Visibility/interest | Prototype | Hardened | Hardened | Hardened | requirements unknown | Requirements first |
| Dropped item | Prototype | Hardened | Hardened | Functional/Hardened | sparse cell only | Useful evidence |
| Falling block | Functional | Functional/Hardened | Functional/Hardened | Functional/Hardened | active multi-tick path | Useful evidence done |
| Mob/second actor | Absent | Functional | Hardened | Functional | no mob/projectile | Yes |
| Navigation/AI | Absent | deliberately limited | Hardened | behavior-specific | after actor foundation | No |
| Actor workload | Absent/in-flight | Scale-tested | Scale-tested | Characterized | player harness only/in flight | Yes |
| Entity/plugin API | Absent intentionally | Production-proven | Production-proven | Production-proven | must remain unfrozen | Must remain unfrozen |

### Where Zenith is ahead / behind / incomparable

**Ahead structurally:** strict gameplay/protocol/packet separation; single-writer authority; reusable owned NBT/LevelDB leaves; documented freezes; inventory conservation tests. This is design quality, not production maturity.

**Behind:** actor breadth, vanilla mechanics, interest management, AI/navigation, operational hardening, Bedrock E2E CI, extensions and accumulated edge cases.

**Not comparable yet:** capacity, TPS, extension ergonomics and entity performance. No committed representative actor workload exists.

## Maturity gaps

| Gap class | Concrete gaps |
|---|---|
| Missing feature | general damage, mobs, projectile, item physics, AI/navigation, effects, richer world interaction |
| Missing architecture | actor lifecycle/identity/replication contract; later interest management; not yet ECS |
| Missing validation | live fall/ISR smoke, automated client smoke in CI, reproducible load report |
| Missing scale proof | actor iteration/churn/query/observer fan-out/memory envelope |
| Missing operations | backup/restore, public hardening, capacity SLOs, alerting, recovery drills |

## Dependency graph

```mermaid
flowchart TD
  R[Runtime ownership and multiplayer correctness] --> V[Reproducible player scale baseline]
  R --> H[General health/damage/death]
  H --> M1[First real mob vertical slice]
  M1 --> M2[Second actor: projectile or equivalent]
  M1 --> ER[Actor replication/lifecycle]
  M2 --> AP[Repeated actor state and lifecycle pressure]
  ER --> VR[Visibility/activation requirements]
  V --> AW[Actor workload baseline]
  AP --> E[Entity Runtime & ECS feasibility spike]
  VR --> E
  AW --> E
  E --> ADR[ADR: accept or reject ECS]
  ADR --> ECS[World-actor ECS only if justified]
  VR --> VIS[Spatial interest implementation, independent]
  ECS -. does not imply .-> VIS
  ECS -. does not imply .-> JOB[Parallel job scheduler]
```

Inventory/container multi-owner transaction pressure is a separate branch. It may share resource invariants with actors but does not justify putting inventory in ECS.

## ECS readiness matrix

**2 DONE, 2 PARTIAL, 9 NOT STARTED.** The count is a checklist, not a forecast; partial does not count as complete.

| Prerequisite | Status | Evidence / done condition |
|---|---|---|
| Runtime/load harness | DONE | committed player harness documents command/fixture and outputs tick, allocation, GC and egress metrics |
| Entity workload baseline | NOT STARTED | non-player simulation plus observer fan-out |
| Dropped-item lifecycle | PARTIAL | cell pickup/TTL exists; free actor lifecycle/physics does not |
| Falling-block lifecycle | DONE | ADR §95, active multi-tick actor, tests/smoke |
| Health/damage | PARTIAL | fall/void only; require shared operation |
| First mob | NOT STARTED | spawn→replicate→act→damage→despawn |
| Second actor behavior | NOT STARTED | projectile/equivalent with different access pattern |
| Projectile/equivalent | NOT STARTED | continuous collision/lifecycle evidence |
| Entity replication | PARTIAL | two bespoke paths; shared actor lifecycle required |
| Visibility requirements | NOT STARTED | tracking/activation semantics + fan-out measurements |
| Repeated actor-state patterns | NOT STARTED | at least three real paths, not invented components |
| Natural component-like queries | NOT STARTED | overlapping queries in direct model |
| Iteration/lifecycle measurable | NOT STARTED | p50/p95/p99, alloc/GC, churn/query costs |

### ECS too early signals

- Components are being named before three actor paths exist.
- The desired result is “clean architecture”, multithreading, or global-fan-out relief rather than a measured simulation limitation.
- Visibility/activation/replication requirements are unknown.
- A generic `Entity → LivingEntity → Mob` tree or a component registry exists only to make the first mob compile.
- The benchmark uses synthetic components unlike real Zenith actors.

### ECS too late signals

- Three or more stores duplicate position/velocity/age/identity/despawn state.
- Overlapping lists or central type switches grow with each actor.
- Two current features are blocked by inheritance/type checks or need the same lifecycle/replication change.
- A public entity/custom-actor API is about to freeze around the premature model.

## Before the decision / after the decision

| Capability | Classification |
|---|---|
| General health/damage ownership | Must precede |
| Actor IDs and spawn/move/remove replication | Must precede |
| First mob + materially different second actor | Must precede |
| Reproducible actor workload and visibility requirements | Must precede |
| Dropped item movement/merge/pickup and falling block | Useful evidence before decision |
| Navigation/pathfinding/AI scheduling | Can follow decision |
| Effects, actor persistence breadth, many mobs | Can follow decision |
| Public plugins/custom actor API | Must wait until after decision |
| Command parsing/registration/autocomplete | Can evolve independently from ECS; plugin command hooks wait |

## ECS feasibility spike

Open the spike only after the gate below. Compare the then-current straightforward world-actor representation against an internal archetype/SoA prototype—never a server rewrite or public API. Use real Zenith mixes (mobs, falling blocks, items, projectile, spawn/despawn, damage and actual observers), at 100, 1,000, 10,000 and 50,000 actors only while previous scales remain meaningful.

Measure tick average/p50/p95/p99/max, CPU, allocation, GC, retained memory, iteration throughput, query cost, structural add/remove, creation/destruction, and replication bytes/fan-out. Start single-writer. Job scheduling needs its own ADR with read/write and structural-change evidence.

**Accept** only if representative workloads materially improve or demonstrated cross-cutting complexity decreases. **Falsify** if simple actors meet the capacity target, the bottleneck is network/visibility/world I/O, or ECS worsens churn/allocation/complexity.

## Revised roadmap rationale

The concise canonical version is [`docs/roadmap.md`](../roadmap.md).

| Phase | Objective | Exit evidence | Unlocks | Explicitly deferred |
|---|---|---|---|---|
| A Runtime proof | close player runtime/scale baseline | reproducible report, invariants, smoke status | honest capacity envelope | ECS, visibility system, jobs |
| B Survival state core | general health/damage/death | source/cause tests, atomic death/drop/respawn, client smoke | actor interaction | hunger/armor/effects breadth |
| C First actor | one mob vertical slice | lifecycle/replication/interaction tests and multiplayer smoke | second actor | inheritance/AI framework |
| D Actor pressure | second actor, workload, visibility requirements | repeated patterns + measurements | ECS decision | speculative visibility impl |
| E Decision | simple vs ECS comparison | benchmark + ADR accept/reject | limited ECS if accepted | players/inventory/jobs in ECS |
| F Scale/behavior | expand actor, AI and interest independently | per-domain gates | extension surface later | public API before stability |
| G Production | E2E CI and operations | beta gate/SLOs/recovery proof | beta decision | parity chase |

## Next three recommended goals

1. **Close Runtime Validation & Multiplayer Scale Baseline.** Proves reproducibility, a defined player 20-TPS envelope, conservation under load and global-fan-out cost; unlocks honest capacity guidance. It explicitly does not add ECS, visibility, jobs or mobs.
2. **Establish a general gameplay health/damage/death primitive.** Proves source/cause attribution, atomic death/drop/respawn and client/multiplayer synchronization; unlocks actor interaction. It does not add hunger, armor/effects, broad combat or an entity hierarchy.
3. **Deliver one real mob vertical slice using the smallest direct actor model.** Proves spawn, active tick, basic behavior, replication, interaction and removal; unlocks second actor, actor benchmark and ECS evidence. It does not add generic Entity, AI framework, ECS, plugins or visibility system.

## ECS decision gate

Open **Entity Runtime & ECS Feasibility Spike** only when all are true:

- The runtime baseline is committed, reproducible and records a defined 20-TPS envelope.
- General damage/health/death is tested, including no partial inventory/drop commit.
- A mob and materially different second actor exist and are observed by multiple clients.
- Actor IDs and spawn/move/remove/rejoin/known-chunk semantics are tested.
- Actor workload includes simulation and replication/observer cost.
- Visibility/activation requirements are documented.
- At least two current features demonstrate repeated state/query/lifecycle pressure.
- No public entity/plugin API has frozen the premature model.
- Benchmark design and ADR question are approved.

## Post-ECS path, only if accepted

Limit it initially to **world actors**: mobs, projectiles, dropped items, falling blocks and similar dynamics. Players, sessions, RakNet, packets/protocol, inventory/containers, storage, commands and plugin runtime remain outside by default. First phase stays single-writer. ECS may unlock composition, actor growth, AI scheduling and later extension seams; it does not solve network visibility or imply parallel execution.

## Direct answers

| Question | Answer |
|---|---|
| Architecture-limited or feature-limited? | Feature-limited first; validation/scale proof next; ECS absence is not the blocker. |
| Mature capabilities before ECS? | General damage, two real actor behaviors, lifecycle/replication, visibility requirements, actor workloads. |
| Mature capabilities that wait? | Broad AI, effects, persistence breadth, plugin API, production ops, jobs and parity. |
| Minimum entity set? | Falling block + improved item/equivalent, one mob, one projectile/second behavior, shared replication/lifecycle tests and workload data. |
| When does waiting become debt? | When ≥3 paths duplicate actor state/stores/type-switches or public entity API is imminent. |
| When is ECS premature? | Now: no mob/projectile/shared lifecycle/visibility requirements/actor benchmark. |
| Is GameLoop a good base? | Yes for single-writer ECS; no implication of parallel ECS. |
| Players in ECS? | Later or potentially never; they are session/inventory-heavy. |
| Outside ECS? | Sessions, RakNet, packets/protocol, inventory/containers, storage, commands, plugin runtime; players by default. |
| Visibility timing? | Requirements precede decision; implementation evolves independently when fan-out warrants it. |
| Worthwhile benchmark? | Real simple-vs-SoA/archetype actors with tail tick/alloc/memory and lifecycle/fan-out measures. |
| Falsifier? | Simple model meets target, bottleneck is network/visibility/I/O, or ECS makes churn/complexity worse. |
| Next goals? | Runtime baseline; general damage; first direct-model mob. |

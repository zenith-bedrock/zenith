# Roadmap: evidence before architecture

**Purpose:** the canonical order of current work. It is a dependency graph with maturity gates, not a PocketMine parity list or a backlog. Architecture constraints remain in [`ARCHITECTURE.md`](../ARCHITECTURE.md); historical rationale stays in [`decisions.md`](decisions.md); the capability assessment is [`audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md`](audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md).

## Current position

Horizon 0 (`v0.0.1-alpha`) and Horizon 1 are closed. Zenith now has a functional multiplayer alpha: authoritative player runtime, world overlays/generation, inventory/chests/crafting, selected persistence, player replication, floor-item wire, falling blocks, death/respawn and fall damage.

The next story is:

```text
working multiplayer
  → measured player runtime
  → complete gameplay primitive (damage)
  → real actor pressure
  → measured actor limitations
  → ECS decision, not ECS by default
```

## Progress states

Use these rather than speculative percentages: **Not started**, **Early**, **Functional**, **Characterized**, **Hardened**, **Proven**. A phase is complete only when every exit criterion is evidenced in source/tests/smoke/measurement.

| Phase | Status at audit baseline | Objective |
|---|---|---|
| A. Runtime proof | Early / gate final | characterize multiplayer runtime and fan-out |
| B. Survival state core | Early | make health/damage/death a usable gameplay primitive |
| C. First actor vertical slice | Not started | learn one real actor lifecycle without generic framework |
| D. Actor pressure and interest requirements | Not started | collect the evidence needed for an ECS decision |
| E. ECS decision | Not started | compare models and record an ADR accept/reject |
| F. Actor scale and behavior | Not started | grow actors, AI and interest management independently |
| G. Production hardening | Early | move from alpha proof to operable beta evidence |

## NOW — Phase A: Runtime proof

**Goal:** close *Runtime Validation & Multiplayer Scale Baseline* without changing application architecture.

**Capabilities:** reproducible in-process player load scenarios, current GameLoop ordering, inventory conservation assertion, tick latency/allocations/GC/egress measurements, real-client smoke status.

**Exit criteria:**

- Load harness is committed, documented and runnable with an explicit command, fixture and player-count matrix.
- Report records p50/p95/p99/max tick, allocation, GC and egress for steady and chunk-burst scenarios.
- The report identifies the measured all-online fan-out limitation without presenting it as an ECS result.
- The defined scenario remains at or below the 50 ms tick budget, or records the saturation point and cause honestly.
- Relevant conservation/lifecycle tests and available smoke-bot/human smoke pass at the recorded revision.

**Unlocks:** an honest capacity envelope and comparable baseline for future actor work.

**Explicitly does not do:** ECS, VisibilitySystem, job scheduler, actor framework, mobs.

## NEXT 1 — Phase B: Survival state core

**Goal:** turn `Player.Health` from a fall/void-only field into one small, authoritative damage/death primitive.

**Prerequisites:** Phase A complete; existing single-writer and inventory conservation constraints hold.

**Exit criteria:**

- A gameplay-owned damage operation records cause/source semantics needed by current gameplay, updates client state and has deterministic ordering.
- Damage, lethal death, respawn and death-drop behavior have tests proving rejection/failure cannot create or lose authoritative resources through a partial commit.
- At least one multiplayer/real-client observation validates the new path.
- No generic `LivingEntity`, event framework, ECS or broad status-effect system is introduced merely for this leaf.

**Unlocks:** a meaningful actor-versus-player interaction vertical slice.

**Deliberately deferred:** hunger, armor, enchantments, broad effects, combat breadth and AI.

## NEXT 2 — Phase C: First actor vertical slice

**Goal:** ship one simple real mob using the smallest direct model that preserves authority and testability.

**Prerequisites:** Phase B damage/death primitive; protocol packet needs cross-checked before implementation.

**Exit criteria:**

- Spawn, runtime ID, active tick, basic behavior/movement, replication, interaction/damage, death/despawn and late join/known-chunk behavior are covered by tests.
- Two real clients observe spawn, movement/state and removal correctly.
- Actor state is owned by the GameLoop; network/async work only publishes bounded inputs/results.
- The implementation avoids both a wide inheritance tree and a pre-spike component registry.

**Unlocks:** a second materially different actor and a representative actor workload.

**Deliberately deferred:** generic Entity hierarchy, navigation framework, broad mob catalogue, ECS, plugin/custom-entity API and VisibilitySystem.

## LATER — Phase D: Actor pressure and interest requirements

**Goal:** create evidence, not an architecture, from a projectile or equivalent second actor and actor workloads.

**Exit criteria:**

- A second actor has materially different lifecycle/access patterns from the mob (prefer projectile: movement, collision, despawn, observer updates).
- The direct model exposes or disproves repeated position/velocity/age/identity/dirty/lifecycle patterns across at least three actor paths, including falling blocks or drops.
- Actor benchmarks report simulation and replication/observer costs at increasing populations; results include p50/p95/p99/max, allocation/GC, memory and churn.
- Tracking/activation/visibility requirements are written down and global fan-out is measured separately from simulation cost.

**Decision unlocked:** `Entity Runtime & ECS Feasibility Spike`.

**Deliberately deferred:** solving visibility by assuming ECS, parallel jobs, public extension API.

## DECISION GATE — Phase E: Entity Runtime & ECS feasibility spike

Open only when all Phase A–D gates hold and the checklist in the [maturity audit](audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md#ecs-decision-gate) is green.

The spike compares the actual straightforward actor model with an internal archetype/SoA design under Zenith workloads. It must measure tail tick times, CPU, allocation/GC, retained memory, iteration/query throughput, creation/destruction, structural changes and replication fan-out. Start single-threaded.

**Outcome:** ADR explicitly accepts or rejects ECS for *world actors only*. A rejected hypothesis is a successful result when the simpler model meets the target. ECS does not imply visibility implementation or parallel scheduling.

## LATER — Phase F: Actor scale and behavior

After the Phase E ADR, evolve these independently as pressure proves each one necessary:

- actor composition and additional mobs;
- navigation/pathfinding and AI scheduling;
- spatial interest/visibility and activation policy;
- actor persistence semantics;
- effects/status and world interaction;
- future custom-actor/plugin extension surface.

Players, sessions, RakNet, packet serialization, inventory/containers, storage and commands remain outside an accepted ECS by default. A job scheduler requires separate contention/ordering evidence and a separate ADR.

## Permitted parallel track — command surface

Commands are not a prerequisite for ECS and must not delay Phases A–D, but they are no longer a frozen subsystem. Expand them when a concrete server operation or gameplay command warrants it, including Bedrock command metadata/autocomplete when the command set makes that useful.

**Boundary:** the command core is protocol-independent: definitions, aliases, typed arguments, overload selection, validation, permissions, suggestions and consistent result/error feedback have no Bedrock packet dependency. A thin Bedrock adapter translates core definitions and suggestions into client command metadata/autocomplete and translates wire input into a core invocation. An accepted command calls a gameplay runtime API or publishes an intent according to ADR §97. A command handler does not directly mutate authoritative world/player/inventory state and Protocol does not decide command semantics.

**Exit criteria for the first command slice:** a deliberately small set of representative commands proves aliases, typed arguments, enums, optionals, ranges, player targets, multiple syntaxes/overloads, permissions, suggestions and consistent feedback/errors. Unit tests cover parse/validation/rejection without Bedrock types; metadata/autocomplete is adapter-tested; one real-client completion path is verified. Do not expose plugin command registration, `IPluginCommand`, dynamic discovery, DI, public hooks or a public command API before the plugin extension boundary is separately justified.

## LATER — Phase G: Production hardening

**Goal:** establish beta-grade operational evidence, independently of feature breadth.

Required gates include automated Bedrock E2E in CI, reproducible release smoke, capacity envelope/SLOs, backup/restore and recovery exercises, public auth/exposure hardening, metrics/logging sufficient to diagnose production failure, and documented upgrade/rollback behavior.

## Intentionally deferred

- Full vanilla parity, redstone/fluids/biome product breadth and Mojang-world import unless a concrete adoption goal requests them.
- Plugin API, DI and public entity/custom-component API until the relevant internal domains stabilize. Command implementation is permitted under the separate command-surface track; plugin-facing command registration remains deferred.
- VisibilitySystem, ECS, actor model, scheduler or job system as standalone cleanup.
- Treating player fan-out, entity simulation and parallel execution as one problem.

## Historical record

H0/H1 shipped details, Cereal migration history, and individual leaf rationale remain in [`decisions.md`](decisions.md) and git history. Do not re-open closed horizons just to append polish. Update this file when a gate closes, a measurement changes the dependency graph, or the ECS ADR changes the decision state.

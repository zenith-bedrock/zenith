# Roadmap: evidence before architecture

**Purpose:** the canonical order of current work. It is a dependency graph with maturity gates, not a PocketMine parity list or a backlog. Architecture constraints remain in [`ARCHITECTURE.md`](../ARCHITECTURE.md); historical rationale stays in [`decisions.md`](decisions.md); the capability assessment is [`audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md`](history/audits/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md).

## Current position

Horizon 0 (`v0.0.1-alpha`) and Horizon 1 are closed. Zenith now has a functional multiplayer alpha: authoritative player runtime, world overlays/generation, inventory/chests/crafting, selected persistence, player replication, floor-item wire, falling blocks, health/death/respawn, fall damage, a twelve-species actor roster (Zombie, Skeleton, Cow, Creeper, Enderman, Bat, Spider, Villager, Golem, Minecart, Fish, Projectile), real player-riding, and — as of Phase XXI/XXII — a real ECS runtime authoritative for six of those twelve (Zombie, Minecart, Projectile, Cow, Skeleton, Spider), with a `DamageDispatch` seam replacing per-species type-switching for cross-category damage.

The next story was:

```text
working multiplayer
  → measured player runtime
  → complete gameplay primitive (damage)
  → real actor pressure
  → measured actor limitations
  → ECS decision, not ECS by default
```

Phase XXI (ADR §106) took the last step for a deliberately small slice as an explicit scope
decision rather than waiting for further measured pressure; Phase XXII (ADR §107) expanded that
slice to six species and closed the ECS-as-active-focus story — see the Phase XXI/XXII sections
below. The 6 remaining `IDamageableActor` species (Creeper, Enderman, Bat, Villager, Golem, Fish)
still follow the original story unchanged: no further ECS migration is scheduled until per-species
evidence or a deliberate scope decision like §106/§107 revisits it.

## Progress states

Use these rather than speculative percentages: **Not started**, **Early**, **Functional**, **Characterized**, **Hardened**, **Proven**. A phase is complete only when every exit criterion is evidenced in source/tests/smoke/measurement.

| Phase | Status at audit baseline | Objective |
|---|---|---|
| A. Runtime proof | Characterized / gate final | characterize multiplayer runtime and fan-out |
| B. Survival state core | Characterized | make health/damage/death a usable gameplay primitive |
| C. First actor vertical slice | Characterized | learn one real actor lifecycle without generic framework |
| D. Actor pressure and interest requirements | Characterized | direct-model actor churn, fan-out, runtime/DX and visibility evidence are recorded |
| E. ECS decision | Deferred with evidence (superseded for one slice) | first isolated comparison found no material gain; retain the direct model for unmigrated species pending named missing evidence |
| F. Entity interest foundation | Characterized | chunk knowledge proves actor replication can exclude irrelevant observers without a visibility framework |
| G. Production hardening | Functional | automated Bedrock smoke, runtime health logs and measured capacity baseline are established; recovery matrix remains in progress |
| XXI. ECS foundation & first migration | Accepted | real ECS built and adopted for Zombie/Minecart/Projectile as an explicit scope decision; DX/benchmarks recorded honestly |

## CLOSED / CHARACTERIZE — Phase C: First actor vertical slice

**Goal:** finish the first concrete Zombie slice and record its remaining lifecycle/replication evidence without introducing a generic actor framework.

**Capabilities:** concrete `Zombie` state/store/system, composed `HealthState`, actor runtime IDs, gameplay-owned tick/lifecycle decisions and Bedrock add/move/remove projection.

**Exit criteria:**

- Zombie spawn, tick, targeting, attack handoff, lethal transition, removal and late-join behavior remain covered by tests.
- A real two-client smoke observes the actor lifecycle and health/movement projection. A
  protocol-valid real-client `click_air` sequence observes `20 → 16 → 12 → 8 → 4 → 0` and
  `remove_entity`; the earlier missed-swing-only probe was characterized as a harness limitation.
- The runtime baseline remains the prerequisite evidence for actor workloads; it is not presented as an ECS result.
- The direct model remains free of a generic `Entity` hierarchy, component registry or visibility framework.

**Unlocks:** a second materially different actor and a representative actor workload.

**Explicitly does not do:** ECS, VisibilitySystem, job scheduler, inheritance hierarchy, broad AI or plugin entity API.

## CLOSED — Phase B: Survival state core

**Goal:** turn `Player.Health` into one small, authoritative damage/death primitive.

**Prerequisites:** Phase A complete; existing single-writer and inventory conservation constraints hold.

**Exit criteria:**

- A gameplay-owned damage operation records cause/source semantics needed by current gameplay, updates client state and has deterministic ordering.
- Damage, lethal death, respawn and death-drop behavior have tests proving rejection/failure cannot create or lose authoritative resources through a partial commit.
- At least one multiplayer/real-client observation validates the new path.
- No generic `LivingEntity`, event framework, ECS or broad status-effect system is introduced merely for this leaf.

**Unlocks:** a meaningful actor-versus-player interaction vertical slice.

**Deliberately deferred:** hunger, armor, enchantments, broad effects, combat breadth and AI.

## CLOSED / CHARACTERIZED — Phase D: Actor pressure and interest requirements

**Goal:** create evidence, not an architecture, from a projectile or equivalent second actor and actor workloads.

**Exit criteria:**

- A concrete snowball has materially different lifecycle/access patterns from the Zombie: velocity, owner/source, collision, impact/lifetime removal and rapid churn.
- Falling block, Zombie and Projectile now expose repeated identity/position/lifetime/tick/remove/replication pressure; FloorDrop is recorded as related but cell/merge-specific evidence.
- Actor-churn measures the real `ProjectileSystem` at 100 and 1,000 actors with one and ten observers, recording p50/p95/p99/max, allocation/GC, churn, fan-out and RakNet egress.
- Visibility/activation requirements and the distinction between simulation, interest and replication are documented. Global fan-out is measured; no visibility implementation is inferred.

**Decision unlocked:** `Entity Runtime & ECS Feasibility Spike`. See exact commands, results and
cost decomposition in [`phase-d-actor-pressure.md`](history/phases/phase-d-actor-pressure.md).

**Deliberately deferred:** solving visibility by assuming ECS, parallel jobs, public extension API.

## CHARACTERIZED / DEFERRED (superseded for one slice) — Phase E: Entity Runtime & ECS feasibility spike

Open only when all Phase A–D gates hold. Phase D is characterized; its evidence is
[`phase-d-actor-pressure.md`](history/phases/phase-d-actor-pressure.md), alongside the
[maturity audit](history/audits/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md#ecs-decision-gate).

The first isolated direct-list versus contiguous-SoA comparison did not establish a material gain
over the direct model. Its explicit missing evidence and reopen conditions are in
[`PHASE_E_ECS_FEASIBILITY_FINDINGS.md`](history/audits/PHASE_E_ECS_FEASIBILITY_FINDINGS.md). This finding is
still accurate as a record of what was measured at the time — it was not overturned by new
measurements.

**Outcome:** ADR §102 defers ECS for *world actors only*. ECS does not imply visibility
implementation or parallel scheduling.

**Superseded, for one deliberately small slice, by Phase XXI (ADR §106):** rather than reopening
this phase with new pressure evidence, Phase XXI made an explicit scope decision to build a real
ECS for Zombie/Minecart/Projectile and evaluate it by production use rather than by a measured
actor-count trigger. See [`docs/ecs.md`](ecs.md) and
[`phase-xxi-ecs-foundation-findings.md`](history/phases/phase-xxi-ecs-foundation-findings.md). This phase's
original conclusion (no material gain was *measured* at the time) remains the correct read of the
evidence that existed then, and remains the standing rule for the 9 species and every other domain
Phase XXI did not touch — see the next section.

## CHARACTERIZED — Phase F: Entity interest foundation

Phase F proves the smallest interest decision needed by the current actor paths. `ActorInterest`
is a Gameplay-owned policy seam that answers only whether a player has confirmed knowledge of an
actor's chunk. Zombie and Projectile reconcile their existing per-observer replicated sets:
entering known columns sends the actor add; leaving sends remove; moves and health updates go only
to known observers. It creates no packets, mutates no actor state and owns no lifecycle.

The 1,000-projectile / 10-observer / 200-tick controlled comparison reduces movement fan-out from
1,980,000 to 198,000 and egress from 68.85 MB to 6.98 MB; see
[`phase-f-entity-interest.md`](history/phases/phase-f-entity-interest.md). The result is evidence for chunk
knowledge, not an ECS, visibility, spatial-index or networking abstraction decision.

**Exit criteria:** relevant/irrelevant observer, enter/leave reconciliation and late join are
covered for the concrete actor paths; the same Phase-D workload records before/after fan-out,
datagrams, bytes, tick time and allocation; the policy boundary is recorded in ADR §103.

## CHARACTERIZED — Phase H: Gameplay vertical-slice foundation

Zombie now proves a complete concrete melee loop: acquire/chase a Player, respect a cooldown,
apply authoritative damage, and preserve the existing Player health/death/respawn transition.
Skeleton is deliberately a separate ranged loop: distance selection, target rotation and a
cooldown request to the existing Projectile lifecycle owner. A projectile from a non-player actor
can damage a Player; the established player snowball behavior remains Zombie-only.

`PlayerDamage` is the one extraction justified by both existing movement damage and concrete mob
damage. It centralizes the already-established Player-only death transition and client feedback;
it is not a generic combat system. Gameplay still decides every target, cooldown, damage amount,
spawn and removal; `EntityProtocol` only projects those outcomes.

Focused unit tests and two-client Bedrock smokes cover Zombie health/removal, Projectile
movement/removal and reconnect. The 100/500/1,000 actor interest benchmark remains a Projectile
churn baseline, and continues to identify observer fan-out rather than simulation representation
as the scale cost. See [`phase-h-gameplay-findings.md`](history/phases/phase-h-gameplay-findings.md).

**Non-goals:** generic mob/actor hierarchy, AI/behavior tree/navigation framework, combat
attributes/effects/armor, ECS, VisibilitySystem, plugin API and scheduler.

## ACCEPTED — Phase XXI: ECS foundation & first authoritative runtime migration

**Goal:** build a real, auditable ECS runtime and migrate a representative actor slice (Zombie,
Minecart, Projectile) onto it as their sole authoritative store, evaluated by production use and
DX rather than by a measured-pressure trigger. Full detail:
[`docs/ecs.md`](ecs.md) (what exists),
[`phase-xxi-ecs-foundation-findings.md`](history/phases/phase-xxi-ecs-foundation-findings.md) (benchmarks, DX
comparison, rejected alternatives, final-questions answers), ADR
[`decisions.md` §106](decisions.md).

**Exit criteria — all met:** ECS core (`EntityId`, `EntityWorld`, `ComponentStore<T>`, `Query`)
with full test coverage including stale-handle safety; Zombie/Minecart/Projectile migrated with
their old `*Store`/concrete classes deleted (not wrapped); full-solution build and
`dotnet test zenith.sln` pass (935 tests); ECS-core and representative-gameplay benchmarks recorded
honestly (including where results were neutral, not just where they favored the migration);
`docs/ecs.md`/findings/ADR/`entities.md` updated without rewriting the still-accurate §99–102
deferral history.

**Explicitly bounded:** the 9 remaining `IDamageableActor` species (Skeleton, Cow, Creeper,
Enderman, Bat, Spider, Villager, Golem, Fish) are untouched and still governed by Phase E's
standing "no material gain measured" conclusion. Players, sessions, inventory, world storage,
packets and transport were never in scope. Archetypes, a job scheduler, and a public plugin-facing
ECS surface were all evaluated and rejected this phase — see the findings doc's Rejected
Alternatives.

**Unlocks:** future migration of individual remaining species, evaluated per-species against the
retirement-gate trigger in `docs/ecs.md`, not scheduled here.

## ACCEPTED — Phase XXII: ECS roster consolidation & damage-dispatch unification

**Goal:** find out whether the Phase XXI ECS DX holds up across materially different gameplay
shapes (passive/breeding, ranged-with-ECS-composition, melee-with-outgoing-effect), and resolve the
two-species damage-dispatch chain Phase XXI explicitly left as temporary. Full detail:
[`docs/ecs.md`](ecs.md) (what exists now),
[`phase-xxii-ecs-roster-consolidation-findings.md`](history/phases/phase-xxii-ecs-roster-consolidation-findings.md)
(benchmarks, DX comparison, final-questions answers), ADR [`decisions.md` §107](decisions.md).

**Exit criteria — all met:** Cow, Skeleton, Spider migrated to ECS-authoritative with their old
`*Store`/concrete classes deleted; Skeleton→Projectile ECS-to-ECS composition proven with no new
coupling type; `DamageDispatch` replaces the hardcoded Zombie/Minecart-only chain in
`ProjectileSystem`; full-solution build and `dotnet test zenith.sln` pass (817 tests);
`GroundMobCombat`/`IDamageableActor` retirement gate explicitly re-evaluated (kept, with an updated
trigger) rather than silently left stale; `docs/ecs.md`/findings/ADR/`entities.md` updated.

**Explicitly bounded:** the 6 remaining legacy species (Creeper, Enderman, Bat, Villager, Golem,
Fish) are untouched — evaluated and deliberately not migrated this phase (see the retirement-gate
accounting in `docs/ecs.md` and ADR §107). Players, sessions, inventory, world storage, packets and
transport remain never in scope. Archetypes, a job scheduler, a generic event bus, and a deferred
structural-mutation command buffer were all evaluated and rejected — none of this phase's evidence
justified them.

**Unlocks:** ECS is now treated as stable runtime infrastructure, not the project's active
architecture focus. Future migration of a remaining legacy species happens when gameplay work
already needs to touch it, not on a schedule. The project's next strategic direction is
**Phase XXIII — World Generation Foundation & Exploration Loop** (not yet started; no findings doc
exists for it yet).

## LATER — Actor scale and behavior

After this foundation, evolve these independently as pressure proves each one necessary:

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

Current beta-foundation evidence is in [`phase-g-beta-readiness.md`](history/phases/phase-g-beta-readiness.md).

## Intentionally deferred

- Full vanilla parity, redstone/fluids/biome product breadth and Mojang-world import unless a concrete adoption goal requests them.
- Plugin API, DI and public entity/custom-component API until the relevant internal domains stabilize. Command implementation is permitted under the separate command-surface track; plugin-facing command registration remains deferred.
- VisibilitySystem, actor model, scheduler or job system as standalone cleanup.
- ECS as standalone cleanup, outside the Phase XXI Zombie/Minecart/Projectile slice (ADR §106) — the 9 remaining `IDamageableActor` species and every non-world-actor domain remain governed by the original Phase E deferral.
- Treating player fan-out, entity simulation and parallel execution as one problem.

## Historical record

H0/H1 shipped details, Cereal migration history, and individual leaf rationale remain in [`decisions.md`](decisions.md) and git history. Do not re-open closed horizons just to append polish. Update this file when a gate closes, a measurement changes the dependency graph, or the ECS ADR changes the decision state.

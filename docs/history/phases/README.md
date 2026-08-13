# Phase report index

Every phase report Zenith has produced, in chronological order. These are **historical evidence**,
not documentation of current behavior — the canonical docs (`docs/architecture.md`, `docs/ecs.md`,
`docs/entities.md`, `docs/decisions.md`, `docs/roadmap.md`) already carry the durable conclusions.
Read a report when you need the underlying measurements, rejected alternatives, or the full
reasoning behind a specific line in `decisions.md`; skip it if you just need to know how Zenith
works today.

Phase lettering (D, E, F, G, H, I, II ... X) predates the later Roman-numeral phases (XI–XXI) —
both sequences are preserved as originally named, not renumbered.

| Phase | Purpose | Key result | Current relevance | Canonical successor |
|---|---|---|---|---|
| [D](phase-d-actor-pressure.md) | Characterize the direct actor model (Projectile churn, fan-out) before any ECS experiment | Recorded p50/p95/p99/allocation/GC/egress baselines at 100/1,000 actors | Historical baseline; unlocked Phase E | [decisions.md §101](../../decisions.md), [roadmap.md](../../roadmap.md) |
| [E](../audits/PHASE_E_ECS_FEASIBILITY_FINDINGS.md) | First direct-list vs. contiguous-SoA ECS feasibility spike | No material gain measured at the time; ECS deferred | Historical — superseded for one slice by Phase XXI (ADR §106), still accurate for the rest | [decisions.md §102, §106](../../decisions.md), [ecs.md](../../ecs.md) |
| [F](phase-f-entity-interest.md) | Should actor replication filter by chunk knowledge? | `ActorInterest` adopted; move fan-out cut ~10x (1,980,000 → 198,000 at 1,000 actors) | Historical evidence backing a still-live mechanism | [decisions.md §103](../../decisions.md) |
| [G](phase-g-beta-readiness.md) | Establish an operational capacity baseline for beta | Tick-budget/capacity envelope recorded | Historical baseline | [roadmap.md](../../roadmap.md) |
| [H](phase-h-gameplay-findings.md) | First complete gameplay vertical slice (Zombie melee, Skeleton ranged, `PlayerDamage`) | Concrete combat loop proven without a generic combat framework | Historical — architecture superseded by Phase XXI for Zombie specifically | [decisions.md §105](../../decisions.md), [ecs.md](../../ecs.md) |
| [I](phase-i-gameplay-foundation-audit.md) | Audit: do Phase-I gameplay primitives already exist? | Yes — closed without new framework work | Historical, audit-only | [roadmap.md](../../roadmap.md) |
| [II](phase-ii-world-interaction-audit.md) | Audit + close two gaps in the survival loop (item/block/inventory) | Two concrete gaps closed | Historical | [roadmap.md](../../roadmap.md) |
| [III](phase-iii-mob-behavior-findings.md) | Extend Zombie movement/behavior pressure | Concrete movement rules characterized | Historical | [entities.md](../../entities.md) |
| [IV](phase-iv-movement-research.md) | Research: compare Zenith's movement model against other Bedrock servers (PocketMine, Dragonfly, etc.) | No adoption — informed later movement decisions | Historical, audit-only | [decisions.md](../../decisions.md) |
| [V](phase-v-actor-movement-replication-findings.md) | Movement replication efficiency for Zombie/Projectile | Projection cost characterized, no framework added | Historical | [ecs.md](../../ecs.md) (replication boundary unchanged) |
| [VI](phase-vi-actor-interest-scaling-findings.md) | Stress-test `ActorInterest` at scale | Confirmed interest gating holds under load | Historical | [decisions.md §103](../../decisions.md) |
| [VII](phase-vii-replication-pipeline-findings.md) | Replication pipeline/scaling foundation audit | Ownership boundary (Gameplay→Protocol→Packets→RakNet) made explicit for actors | Historical, boundary still current | [architecture.md](../../architecture.md) |
| [VIII](phase-viii-actor-simulation-activation-findings.md) | Does actor simulation need a sleep/deactivation primitive? | No — not measured as necessary | Historical | [roadmap.md](../../roadmap.md) |
| [IX](phase-ix-world-gameplay-foundation-findings.md) | Audit: do Phase-IX world-gameplay primitives already exist? | Yes — closed without rebuilding | Historical, audit-only | [roadmap.md](../../roadmap.md) |
| [X](phase-x-player-gameplay-findings.md) | Player gameplay completeness audit | First complete network-input→gameplay→persistence loop confirmed | Historical | [architecture.md](../../architecture.md) |
| [XI](phase-xi-survival-progression-findings.md) | Survival & progression (hunger, XP/leveling) | Both systems shipped as small, bounded gameplay primitives | Historical | [decisions.md](../../decisions.md) |
| [XII](phase-xii-architecture-review.md) | Full architecture consolidation review | Several justified consolidations (see doc); no framework introduced | Historical | [architecture.md](../../architecture.md) |
| [XIII (inventory)](phase-xiii-inventory-action-review.md) | Analysis-only: is the inventory/action model adequate? | Yes, model adequate; one boundary flagged for future evolution | Historical, analysis-only | [decisions.md](../../decisions.md) |
| [XIII (runtime)](phase-xiii-runtime-maturity-review.md) | Runtime hardening & DX review across 7 areas | Real evidence-driven fixes only where repetition/allocation was measured | Historical | [architecture.md](../../architecture.md) |
| [XIV](phase-xiv-gameplay-expansion-findings.md) | Choose and build the next entity (Cow) to pressure gameplay boundaries | Cow shipped; entity-addition pattern validated | Historical | [entities.md](../../entities.md) |
| [XIV.1](phase-xiv-ground-mob-combat-consolidation.md) | Extract `GroundMobCombat` after 3 identical consumers | `IGroundMob`/`GroundMobCombat` extracted (the "3 is a pattern" rule in practice) | Historical — the pattern is still live, unmigrated species | [entities.md](../../entities.md), [ecs.md](../../ecs.md) |
| [XV](phase-xv-entity-behavior-expansion-findings.md) | Validate `IGroundMob`/`GroundMobCombat` against 2 more species (Creeper, Enderman) | Abstraction held without modification | Historical | [entities.md](../../entities.md) |
| [XVI](phase-xvi-entity-runtime-findings.md) | Entity runtime & gameplay depth (breeding, knockback, despawn-when-unseen) | Produced `docs/entities.md` itself | Historical — successor doc is canonical and current | [entities.md](../../entities.md) |
| [XVII](phase-xvii-runtime-pressure-findings.md) | Bat (flight), Spider (2nd chase-mob), Villager (inventory-shaped NPC) | None of the 3 pressure candidates crossed the extraction bar | Historical | [entities.md §7](../../entities.md) |
| [XVIII](phase-xviii-runtime-pressure-findings.md) | Real entity interaction wire path + Golem boss | Interaction was a dispatch gap, not a missing abstraction | Historical | [entities.md §8](../../entities.md) |
| [XIX](phase-xix-runtime-pressure-findings.md) | Minecart (first non-mob category) + Spider poison (first mob-sourced status effect) | Category split held; `EffectSystem` needed zero changes | Historical | [entities.md §9](../../entities.md) |
| [XX](phase-xx-runtime-relationships-findings.md) | Real riding, Fish (3rd movement model), `ViewerReconciliation` extraction, `IGroundMob`→`IDamageableActor` rename | 12-consumer duplication resolved; naming updated | Historical — naming/extraction still current | [entities.md §10](../../entities.md) |
| [XXI](phase-xxi-ecs-foundation-findings.md) | Build and adopt a real ECS for Zombie/Minecart/Projectile | ECS accepted as an explicit scope decision; full DX/benchmark comparison | Major architectural transition — still the primary detailed evidence for the current ECS | [ecs.md](../../ecs.md), [decisions.md §106](../../decisions.md), [entities.md §11](../../entities.md) |
| [XXII](phase-xxii-ecs-roster-consolidation-findings.md) | Migrate Cow/Skeleton/Spider; replace Projectile's 2-species damage type-switch | ECS roster doubled to 6; `DamageDispatch` seam added; `GroundMobCombat` retirement gate re-evaluated and kept | ECS closed as active focus — next phase moves to World Generation | [ecs.md](../../ecs.md), [decisions.md §107](../../decisions.md), [entities.md §12](../../entities.md) |

## Audits

Point-in-time audits live in [`../audits/`](../audits/), not here — they assess repository/capability
state rather than report on one phase's gameplay work. See that directory for the ECS feasibility
spike (Phase E's evidence), the repository/package topology audit, and the pre-Phase-XXI maturity
assessment.

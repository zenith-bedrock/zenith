# ADR §106 — Phase XXI accepts a real ECS for a deliberately small, representative actor slice

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §106`.

> **Superseded in part by §149:** `IDamageableActor`, `GroundMobCombat`, `DamageableActorCombat` and the per-species stores described here no longer exist; every species is an ECS actor on one replication path.

**Choice:** build and adopt a real, auditable, non-archetype ECS runtime
(`src/zenith/Ecs/`: `EntityId`, `EntityWorld`, `ComponentStore<T>`, `Query`/`Query2`/`Query3`,
`RuntimeIdIndex`, `EntityRuntime`) and migrate three actors — Zombie, Minecart, Projectile — onto
it as their sole authoritative store for Position/Health/Velocity. The remaining nine
`IDamageableActor` species (Skeleton, Cow, Creeper, Enderman, Bat, Spider, Villager, Golem, Fish)
are deliberately untouched and remain served by the existing `IGroundMob`-derived
(`IDamageableActor`)/`GroundMobCombat`/`GroundMobMovement`/`DespawnLifecycle` pattern. Full detail:
[`docs/ecs.md`](../ecs.md) (what exists), [`docs/phase-xxi-ecs-foundation-findings.md`](../history/phases/phase-xxi-ecs-foundation-findings.md)
(benchmarks, DX comparison, rejected alternatives, final-questions answers),
[`docs/entities.md`](../entities.md) §11.

**Why now, and why this does not overturn §99–102:** §99–102 deferred ECS as an *evidence-gated*
decision — build it only once measured world-actor pressure justified it, and the first isolated
feasibility spike (`docs/audit/PHASE_E_ECS_FEASIBILITY_FINDINGS.md`) found no material gain over
the direct model at the scales actually observed. That reasoning was correct given what was
measured at the time and is **not retroactively wrong** — nothing about the measured pressure
changed between §102 and this ADR. What changed is the nature of the decision: this is not another
feasibility spike arriving at a data-driven "the evidence now says yes." It is an explicit
architecture decision to build a real ECS for a representative slice and evaluate it by production
use — correctness, developer experience, whether it makes the remaining nine species easier or
harder to migrate — rather than continue waiting for a specific actor count to force the question.
§99–102 remain historically accurate as the record of that earlier, still-valid reasoning; they are
preserved unedited above.

**Why this slice:** Zombie is the ordinary case, representative of 8 of the 9 remaining species.
Minecart has no AI/targeting and a real player-riding relationship, chosen specifically to prevent
the ECS from becoming "a mob ECS." Projectile has no `IDamageableActor`/`HealthComponent` at all,
chosen specifically to prove ECS composition is broader than "things with health." Together they
exercise the ECS across the genuine variety already present in the roster without requiring a
full-roster migration to prove the point.

**What was measured, honestly:** zero allocation at steady state for ECS-core operations (entity
lifecycle, component-store query iteration, runtime-id lookup) at every scale tested (100/1,000/
10,000 entities); full-tick gameplay and replication numbers for the now-ECS-backed Zombie/
Projectile are consistent in shape with pre-ECS Phase D/F baselines — no regression, no dramatic
win, because storage was never the measured bottleneck and remains not the bottleneck now. No
benchmark was run or reported to make the ECS look better than it is; see the findings doc's
Benchmarks and Final Question 6 sections.

**Boundary, preserved from the original brief and unchanged by this ADR:** Gameplay decides →
Protocol transmits → Packets serialize → RakNet transports. `GameLoop` remains single-writer,
single-threaded, deterministic registration order — no job scheduler or parallel execution was
introduced. No inheritance hierarchy (`BaseEntity`/`LivingEntity`/etc.) exists anywhere in the ECS
or its consumers. `ViewerReconciliation.Sync` (§10 above) remains the one replication seam for
every category, ECS-backed or not — it was not replaced by a `ReplicationSystem`. Archetypes, a job
scheduler, source generation/reflection-driven components, and a public plugin API were all
considered and explicitly deferred again — see the findings doc's Rejected Alternatives; none of
this ADR's evidence justifies them.

**Non-goals, restated for this specific decision:** this ADR does not extend to Player, sessions,
inventory, world storage, packets, or transport — it is a world-actor runtime for the migrated
slice only. It does not schedule migration of the remaining nine species on any timeline; see
`docs/ecs.md`'s retirement-gate section for the concrete (if not numerically precise) trigger for
when `GroundMobCombat`/`IDamageableActor` should actually be retired, and the findings doc's Final
Question 10 for why "what migrates next" is deliberately left to future evidence rather than
scheduled here.

# ADR §102 — Defer world-actor ECS after the first isolated Phase-E comparison

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §102`.

**Choice:** **DEFER ECS.** The isolated direct-list versus contiguous-SoA experiment is recorded in
[`PHASE_E_ECS_FEASIBILITY_FINDINGS.md`](../history/audits/PHASE_E_ECS_FEASIBILITY_FINDINGS.md). It did not
show a material simulation/allocation improvement at 100 or 1,000 mixed actors, while Phase-D
production evidence still identifies observer projection/wire fan-out as the dominant incremental
cost.

**Why:** The prototype intentionally remains incomplete as a runtime candidate: it lacks
generation-safe row mapping and the equivalent Projectile→Zombie query. Its compact removal/churn
is measured, but it creates no demonstrated DX or lifecycle advantage over the direct model.
Accepting a production ECS from that evidence would be preference, not an architecture decision.

**Reopen condition:** an isolated, still single-writer comparison with those missing semantics and
identical packet construction/projection; acceptance still requires material gain beyond wire fan-out.

**Non-goals remain:** no production migration, EntityWorld/registry/public query/components, plugin
API, VisibilitySystem, jobs or parallel scheduler.

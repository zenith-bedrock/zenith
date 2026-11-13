# ADR §103 — Use confirmed chunk knowledge as the first actor-interest policy

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §103`.

**Choice:** `ActorInterest` is a small Gameplay decision seam, not a `VisibilitySystem`. It
answers only whether an in-game, living observer has confirmed the actor's current chunk through
`PlayerChunkTracker`. Concrete Zombie and Projectile systems retain their own replicated-pair
bookkeeping and reconcile it to send Add on enter, RemoveActor on exit, and movement/health only
to known observers.

**Why:** Phase-D's 1,000 actor / ten observer workload identified replication fan-out and wire
work as the dominant incremental cost; Phase-E deferred ECS. The Phase-F controlled comparison
holds actor state and GameLoop ordering constant and reduces movement fan-out 1,980,000→198,000,
egress 68.85 MB→6.98 MB and mean tick 53.564→8.601 ms when only one of ten observers knows the
actor column. Existing chunk tracking supplies enough information for this concrete requirement.

**Boundary:** ActorInterest does not create packets, mutate gameplay, select damage targets or
control actor lifetime. `EntityProtocol` remains the transmitter. The policy is not a spatial
query API, spatial index, activation system or networking abstraction; actor simulation continues
without observers.

**Non-goals:** ECS, generic VisibilitySystem, octree/spatial tree, ActorStore/EntityStore, public
components/query/plugin APIs and parallel scheduler. Evolve the policy only after a measured,
named interest rule exceeds confirmed chunk knowledge.

**Verification:** focused actor-interest/reconciliation tests plus
[`phase-f-entity-interest.md`](../history/phases/phase-f-entity-interest.md)'s reproducible real-runtime benchmark.

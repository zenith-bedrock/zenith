# ADR §101 — Phase D records actor pressure before any entity-runtime choice

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §101`.

**Choice:** Close the actor-pressure evidence gate with the direct model intact. The second actor is a concrete, short-lived snowball Projectile; it keeps ownership, velocity, collision, impact/lifetime removal and source attribution concrete. The workload runs production single-writer `GameLoop` ordering with real Projectile simulation and protocol/RakNet projection at 100 and 1,000 actors under one and ten observers. It records tail ticks, allocation/GC, churn, actor fan-out and egress. The full matrix, commands and cost decomposition are in [`phase-d-actor-pressure.md`](../history/phases/phase-d-actor-pressure.md).

**Why:** FallingBlock, Zombie and Projectile independently repeat enough active-state, identity, position/lifetime, tick/remove and viewer bookkeeping to justify testing another representation. The 1,000-actor/10-observer p95 (60.556 ms) is above the 20-TPS 50-ms budget while the one-observer path is far lower; that is evidence for separately measuring actor simulation and global replication/interest, not evidence that ECS solves visibility.

**Non-goals:** accepting ECS; `ActorStore`, `EntityStore`, interface hierarchy, component registry, `VisibilitySystem`, pooling framework, jobs/parallel simulation, public actor/plugin API, or command changes. The future experiment remains world-actors only; players, sessions, RakNet, Packets, Protocol, inventory, containers, persistence and commands remain out of scope by default.

**Next decision:** Phase E may compare the direct world-actor model with an internal, single-writer archetype/SoA prototype under the exact creation/removal, collision/damage and observer workloads. It must end with an ADR accept/reject; rejection is valid.

**Status (Aug 2026):** Evidence gate closed; ECS feasibility spike authorized, ECS not accepted.

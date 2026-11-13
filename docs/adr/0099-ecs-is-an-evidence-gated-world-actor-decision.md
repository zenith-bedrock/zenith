# ADR §99 — ECS is an evidence-gated world-actor decision, not the next architecture step

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §99`.

**Choice:** Do not introduce ECS, a generic `WorldEntity` hierarchy, a VisibilitySystem, or a job scheduler as a response to the completed multiplayer spine. First complete the evidence path recorded in [`roadmap.md`](../roadmap.md) and the maturity audit: reproducible runtime baseline; general damage/death; first real mob; a materially different second actor; actor lifecycle/replication and visibility requirements; then measured actor workload.

If a spike is opened, it compares the straightforward actor model against an internal archetype/SoA design for real Zenith workloads. It starts single-writer on `GameLoop`, applies only to dynamic world actors (mobs, projectiles, dropped items, falling blocks), and ends in an explicit ADR acceptance **or rejection**. Rejection is valid when the simpler model meets its capacity and maintenance targets.

**Why:** the existing falling-block and floor-item paths are valuable evidence but are not enough to infer common component access patterns. Mature references demonstrate that entity simulation, spatial visibility and parallel scheduling are related but independent costs: Dragonfly maintains chunk viewers and actor ticking separately; PocketMine and PowerNukkitX carry substantial entity lifecycle and extension surfaces. Copying any of those models before Zenith has its own actor pressure would freeze guesses.

**Non-goals:** putting players, inventories, containers, sessions, RakNet, packets/protocol, storage, commands or a public plugin API into ECS by default; using ECS as a substitute for interest management; assuming ECS permits safe parallel execution.

**Verification:** gate checklist and benchmark design in [`audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md`](../history/audits/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md). No runtime implementation is authorized by this ADR.

**Status (Aug 2026):** Decision recorded; ECS spike not yet open.

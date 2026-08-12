# Phase F — entity interest foundation

**Decision:** retain a small chunk-knowledge policy for the current concrete actor paths. Do not
introduce a `VisibilitySystem`, spatial tree, ECS or networking abstraction.

## Scope and ownership

`Gameplay/ActorInterest.cs` answers one decision: whether an in-game, living observer has already
received the actor's world column. `ZombieSystem` and `ProjectileSystem` keep their existing
`(actorId, playerId)` replication knowledge and reconcile it every tick: Add when interest begins,
RemoveActor when it ends, and project movement/health only while known.

The seam does not construct packets, change actor position/health/lifetime, select damage targets,
or own actor storage. Gameplay decides relevance; `EntityProtocol` transmits the already-decided
add/move/remove/health outcomes; Packets serialize; RakNet transports.

`PlayerChunkTracker` remains the source of confirmed-column knowledge. A negative radius remains
an explicit harness-only unbounded view so the pre-Phase-F global baseline can be measured without
adding a second runtime implementation.

## Correctness evidence

- `ActorInterestTests`: known in-game column is accepted; unrelated column is rejected; the
  explicit unbounded fixture view remains supported.
- `ProjectileSystemTests`: irrelevant observer receives no initial spawn; publishing the column
  adds it; forgetting it removes it; crossing a chunk boundary removes the old observer and adds
  the new one.
- Existing Zombie and Projectile late-join tests now publish the relevant column explicitly.
  Zombie health/removal and Projectile movement/removal regressions remain covered.

This is observer reconciliation only. Actor simulation keeps running even with no interested
observer, and each concrete system still owns its own lifecycle.

## Reproducible 1,000 × 10 benchmark

```bash
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --players 1 --actors 1000 --actor-players 10 --ticks 200
```

The harness runs two variants through the same production `GameLoop`, `ProjectileSystem`, protocol
encoders and recording RakNet transport. Both start the same 1,000 clustered projectiles at Y=100,
use their real 80-tick lifetime and replenish them for two churn cycles. `actor-global` gives all
ten observers the explicit unbounded fixture view. `actor-interest` gives one observer the actor
column and places the other nine observers' normal chunk streams far from it. Thus simulation and
actor state are held constant; only observer knowledge differs.

Measured 2026-08-11 on Windows 10.0.26200 x64, .NET SDK 10.0.302 / runtime 10.0.10:

| Variant | avg tick | p95 | alloc/tick | datagrams | bytes | spawn fan-out | movement fan-out | remove fan-out |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| global | 53.564 ms | 71.020 ms | 11,407,529 B | 51,780 | 68,851,390 B | 30,000 | 1,980,000 | 20,000 |
| chunk interest | 8.601 ms | 12.165 ms | 1,262,316 B | 5,357 | 6,979,360 B | 3,000 | 198,000 | 2,000 |

The observed dominant cost is projection/fan-out/packet construction and encoded egress for
irrelevant observers. Chunk knowledge reduces that work by about 90% in this workload. It does
not make a claim about general spatial queries, actor activation, pathfinding, collision, or
network transport itself; it also does not reopen the Phase-E ECS deferral.

## Direction

For current Zenith workloads, chunk knowledge is sufficient because the server already has
confirmed-column state and concrete actor paths only need enter/leave reconciliation. Evolve this
policy only when evidence shows a specific missing rule (for example, a different activation range
or non-column relevance). Do not turn it into a generic visibility framework in anticipation of
that pressure.

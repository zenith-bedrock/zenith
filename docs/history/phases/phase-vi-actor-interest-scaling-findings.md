# Phase VI — Actor interest scaling and replication pressure

## Scope

Phase VI stress-tested the existing `ActorInterest` policy and the concrete Projectile
reconciliation path. It does not introduce a `VisibilitySystem`, spatial tree, replication
framework, ECS or actor abstraction.

The policy remains:

```text
PlayerChunkTracker confirmed columns
              |
        ActorInterest decision
              |
   concrete actor Add/Move/Remove
              |
       EntityProtocol / RakNet
```

Simulation continues for actors without interested observers. Relevance only controls projection.

## Workload matrix

The benchmark command is:

```powershell
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --interest-scaling --actors 1000 --actor-players 10 --ticks 40
```

It seeds stationary Projectiles, so movement suppression does not hide interest transitions. The
three layouts are:

- `clustered`: all 1,000 actors occupy the same local chunk; only observer 0 knows that chunk for
  the 10-observer run;
- `distributed`: actors occupy a grid of chunks and observers know a local radius around separate
  chunks;
- `movingobservers`: observers change their known chunk every five ticks, exercising enter/leave
  reconciliation against the same actor population.

The output reports simulation tick time, allocations, datagrams, bytes, spawn fan-out, movement
fan-out, skipped movement and remove fan-out. Spawn/remove fan-out is the measured enter/leave
signal for the concrete actor path.

## Evidence — 1,000 actors × 10 observers

Measured with the Debug in-process production GameLoop harness:

| layout | avg tick | p95 | allocations/tick | datagrams | bytes | spawn/enter | movement | skipped | remove/leave |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| clustered | 14.742 ms | 20.351 ms | 1,318,806 B | 1,137 | 1,460,201 B | 1,000 | 39,000 | 1,000 | 0 |
| distributed | 4.195 ms | 4.504 ms | 240,595 B | 400 | 84,405 B | 58 | 2,262 | 58 | 0 |
| moving observers | 3.160 ms | 3.756 ms | 261,463 B | 400 | 102,611 B | 200 | 2,322 | 58 | 142 |

The clustered result is dominated by relevant-observer fan-out: one observer receives every actor
but all ten systems still evaluate interest. The distributed result projects only the actors whose
chunks are known by each observer. The moving result adds explicit enter/leave churn but remains
below the clustered wire work because observers spend most ticks away from the actor population.

The 1,000 actors × 50 observers stress run (20 ticks) produced:

| layout | avg tick | allocations/tick | datagrams | bytes | spawn/enter | movement | remove/leave |
|---|---:|---:|---:|---:|---:|---:|---:|
| clustered | 30.505 ms | 1,619,944 B | 1,078 | 1,229,575 B | 1,000 | 19,000 | 0 |
| distributed | 16.806 ms | 416,936 B | 456 | 145,575 B | 96 | 1,824 | 0 |
| moving observers | 15.871 ms | 739,061 B | 1,050 | 324,607 B | 610 | 5,578 | 316 |

The 50-observer run makes the scaling shape clear: relevant fan-out, not actor storage or an
unbounded visibility query, is the dominant cost. Moving observers add reconciliation work, but
the concrete chunk policy still limits packet creation to known chunks.

## Lifecycle correctness

Existing and extended leaf tests cover:

- many actors sharing a relevant chunk through benchmark fan-out;
- actors crossing from one known chunk to another;
- late join receiving the current actor state;
- observer leaving interest receiving RemoveActor;
- observer re-entering interest receiving AddActor again;
- reconnect (`IsInGame` false → true) clearing stale knowledge and re-spawning the actor.

No actor lifetime or gameplay state is changed by the interest decision.

## Interpretation

1. **Does chunk knowledge remain sufficient?** Yes for the current requirement. Confirmed chunk
   knowledge expresses the only relevance rule currently demonstrated: whether an observer has the
   actor's world column.
2. **What is the real pressure?** Fan-out to relevant observers and packet/byte production. The
   clustered 50-observer case approaches the 20-TPS budget even though the actor state is static.
3. **Is a spatial tree justified?** No. Actors are already projected by concrete systems and the
   policy lookup is a chunk membership check. The measured cost is downstream fan-out, not finding
   nearby actors through an expensive spatial query.
4. **What is the smallest next primitive?** If pressure persists, first measure a bounded per-tick
   observer/actor projection budget or batch projection work at the concrete system boundary. That
   is a replication scheduling decision, not a generic visibility framework. A spatial index becomes
   justified only when a named relevance rule requires distance/shape queries beyond confirmed
   chunks.
5. **What remains out of scope?** `VisibilitySystem`, octree/spatial tree, ECS, activation policy,
   generic replication API, parallel scheduler and networking abstraction.

## Decision

Keep `ActorInterest` small and chunk-based. Do not replace it after this benchmark. Reopen the
policy only when a concrete feature requires a relevance rule that chunk knowledge cannot answer,
or when profiling proves the membership/reconciliation loop itself—not packet fan-out—is the
dominant cost.

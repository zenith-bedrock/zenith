# Phase VIII — Actor simulation activation pressure

## Question

Does the current concrete actor simulation need sleeping/deactivation before introducing an
activation primitive?

The experiment changes no production actor behavior. It runs the existing GameLoop with concrete
`ZombieSystem` or `ProjectileSystem` and compares the same actor population under three observer
conditions:

- `observed`: one in-game observer near the actor cluster;
- `noobservers`: the synthetic player is not in game;
- `farobservers`: the player is in game but has no confirmed actor chunks.

Diagnostics record the concrete system timing (`tick.system.zombie` or `tick.system.projectile`),
while the harness records total tick, allocations, GC and transport egress.

## Command

```powershell
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --activation-pressure --actors 1000,5000,10000 --actor-players 1 --ticks 10
```

## Evidence

Debug in-process sample, one observer, ten measured ticks. Values are average system time and
average total tick time; the first-run JIT outlier is visible in p95 and is not used as a capacity
claim.

| actor | scenario | Zombie system | total tick | allocations/tick | egress |
|---:|---|---:|---:|---:|---|
| 1,000 | observed | 4.395 ms | 6.968 ms | 203,516 B | 7 datagrams / 7,307 B |
| 1,000 | no observers | 0.406 ms | 0.425 ms | 193,025 B | 0 / 0 B |
| 1,000 | far observers | 0.709 ms | 0.727 ms | 199,612 B | 9 / 9,006 B |
| 5,000 | observed | 2.741 ms | 2.765 ms | 969,575 B | 7 / 7,307 B |
| 5,000 | no observers | 2.355 ms | 2.371 ms | 961,025 B | 0 / 0 B |
| 5,000 | far observers | 2.495 ms | 2.528 ms | 967,614 B | 9 / 9,006 B |
| 10,000 | observed | 3.152 ms | 3.170 ms | 1,929,578 B | 7 / 7,308 B |
| 10,000 | no observers | 3.864 ms | 3.879 ms | 1,921,025 B | 0 / 0 B |
| 10,000 | far observers | 4.138 ms | 4.157 ms | 1,927,612 B | 9 / 9,006 B |

The first 1,000-observer sample has startup noise; the stable 5,000/10,000 samples show that
Zombie simulation remains close to linear in actor count even when no observer can receive an
actor. Interest suppresses egress, not simulation. Far observers have nearly the same simulation
cost as no observers.

Projectile samples were also run. They are bounded by the existing concrete `ProjectileStore`
soft cap of 2,048, so 5,000 and 10,000 requested projectiles produce 2,048 active actors rather
than the requested population. This is a lifecycle capacity guard, not evidence for an activation
policy. At 1,000 projectiles, system time was 5.844 ms observed, 0.445 ms with no observers and
0.713 ms with a far observer. At the 2,048 cap, observed was 14.338 ms, no observers 0.866 ms,
and far observers 0.964 ms.

## Decision

**Next primitive: nothing for now.**

The data proves that interest is not a simulation activation rule: actors without relevant
observers still execute the complete concrete system loop. However, the measured Zombie cost at
10,000 actors is about 4 ms per tick in this synthetic workload, and no correctness or capacity
threshold currently requires sleeping. The dominant Phase VII pressure remains projection/fan-out
when actors are relevant; this Phase VIII experiment does not justify an `ActivationSystem`, AI
scheduler, chunk activation engine or ECS migration.

Revisit simulation throttling only when a production-shaped workload demonstrates a sustained
simulation budget breach. If that happens, test a concrete, actor-specific sleeping rule first,
with explicit wake-up and damage/interaction semantics. Do not infer that distance or interest
alone permits skipping gameplay simulation.

## Scope exclusions

No activation framework, generic actor API, ECS, scheduler, spatial index, visibility system or
networking abstraction was added.

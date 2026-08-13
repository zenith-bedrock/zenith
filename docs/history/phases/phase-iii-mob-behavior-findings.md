# Phase III — Concrete mob behavior and movement pressure

## Scope

Phase III extends the existing concrete Zombie slice. It does not reopen the Phase-I/II item,
drop, persistence or block-interaction decisions and does not introduce an AI or navigation
framework.

The preserved ownership is:

```text
Gameplay decides target, movement, attack and removal.
Protocol projects decided actor state.
Packets serialize the projection.
RakNet transports it.
Diagnostics observes completed ticks.
```

## Existing mob behavior (characterized)

`ZombieSystem` and `SkeletonSystem` both own concrete stores, health/lifecycle and ActorInterest
reconciliation, but their decisions remain different:

- Zombie closes distance and attacks in melee.
- Skeleton maintains a minimum ranged distance and asks `ProjectileSystem` to own each shot.
- Both already use the authoritative `PlayerDamage` path and concrete loot preflight.

Before this phase, Zombie selected the nearest valid player every tick and moved through any block.
Skeleton was intentionally left ranged and world-direct; no shared behavior primitive was inferred
from the fact that both actors have a position.

## Implemented concrete behavior

Zombie now stores `TargetPlayerRuntimeId` as a concrete gameplay fact. Each tick it:

1. retains that player while `IsInGame`, alive and within the 24-block detection range;
2. clears the identity when the player disconnects, dies or leaves the range;
3. acquires the nearest currently valid player when no target is retained;
4. probes the desired movement cell and a bounded pair of local side alternatives;
5. moves only when the feet and head cells are air and the cell below provides support;
6. checks attack range and calls the existing `PlayerDamage` operation.

The side probe is deliberately local and concrete. It does not step through solid geometry, mutate
world state, schedule work or create packets. There is no `MobBrain`, `Pathfinder`,
`NavigationSystem`, shared entity base or async worker.

## World-query primitives reused

The movement rule reuses `World.GetBlock`, `World.AirRuntimeId` and the existing flat terrain
support convention. It uses no packet or Protocol type. `ActorInterest` remains only a replication
decision; Zombie simulation and target selection execute even when no observer knows the actor's
chunk.

## Target lifecycle and tests (implemented)

Leaf tests in `ZombieSystemTests` cover:

- nearest acquisition and existing movement;
- retained target invalidation and reacquisition of another valid player;
- target clearing when the only player is no longer in-game;
- a full-height solid obstacle causing a bounded local side-step while preserving valid body space;
- existing melee cooldown, lethal removal/loot and replication regression coverage.

The tests verify identity by runtime id, not by a retained object reference. No stale player can be
attacked after it leaves the valid online state.

## Diagnostics and runtime characterization (characterized)

The benchmark command is:

```powershell
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --zombie-behavior --actors 100,500,1000 --actor-players 10 --ticks 100
```

It runs the production GameLoop with ten players and ActorInterest enabled. `idle` places Zombies
outside target range and confirmed observer chunks; `direct` clusters them near player zero; and
`obstacle` adds a bounded wall to exercise local probes. It reports wall-clock tick distribution,
allocation/GC, egress and Zombie system timing/fan-out separately.

| mode | actors | avg / p95 tick | Zombie avg | alloc/tick | fan-out (spawn / move / remove) | egress |
|---|---:|---:|---:|---:|---:|---:|
| idle | 100 | 0.756 / 0.533 ms | 0.396 ms | 33,330 B | 0 / 0 / 0 | 140 datagrams / 93,692 B |
| direct | 100 | 0.863 / 1.367 ms | 0.789 ms | 65,976 B | 100 / 2,464 / 100 | 262 / 193,651 B |
| obstacle | 100 | 0.523 / 1.005 ms | 0.492 ms | 68,314 B | 101 / 2,679 / 101 | 259 / 200,481 B |
| idle | 500 | 1.777 / 2.675 ms | 1.743 ms | 125,923 B | 0 / 0 / 0 | 140 / 93,692 B |
| direct | 500 | 2.362 / 4.065 ms | 2.323 ms | 282,258 B | 500 / 12,064 / 500 | 536 / 573,941 B |
| obstacle | 500 | 2.955 / 6.565 ms | 2.916 ms | 293,641 B | 501 / 13,079 / 501 | 551 / 606,411 B |
| idle | 1000 | 2.909 / 3.778 ms | 2.883 ms | 241,923 B | 0 / 0 / 0 | 140 / 93,692 B |
| direct | 1000 | 4.351 / 9.460 ms | 4.318 ms | 549,378 B | 1,000 / 23,800 / 1,000 | 884 / 1,041,385 B |
| obstacle | 1000 | 4.378 / 8.951 ms | 4.345 ms | 571,883 B | 1,001 / 25,815 / 1,001 | 923 / 1,105,951 B |

These are synthetic characterization numbers, not public capacity claims. The key separation is
clear: idle simulation still scans actors but produces no actor projection; direct and obstacle
workloads add movement and observer fan-out. At 1,000 actors, local obstacle probes add roughly
4% allocation and 8% movement fan-out over direct in this sample, while replication remains a
separate visible cost.

## Bedrock E2E (implemented/validated)

The existing two-client `smoke:zombie` was updated to move observer B beyond the concrete aggro
range before asking Zombie to retarget A. This reflects the new retention rule rather than relying
on an implicit nearest-target switch. The smoke passed:

```text
OK: zombie rid=3 moved; both clients observed health, one removal, and death loot
```

It correlates actor runtime identity, movement, health, removal and loot rather than asserting a
fixed packet count. The external smoke repository still has unrelated working-tree changes; the
Phase-III semantic adjustment is isolated there and is not part of the Zenith commit.

## Reference comparison

Dragonfly's local reference separates break/use behavior and uses richer movement/entity
components, including support and collision concepts. Those references confirm the observable
need to reject movement through solid cells and maintain a target validity window. They do not
justify importing Dragonfly's entity hierarchy, handler events or navigation machinery into
Zenith. BetterAltay and other local references likewise catalogue broader AI/entity abstractions,
which remain outside this concrete experiment.

## Pressure audit

| Question | Conclusion |
|---|---|
| State composition | Zombie and Skeleton repeat identity/pose/health, but their behavior decisions remain materially different; no shared representation reduced current complexity. |
| Query pressure | `World.GetBlock` plus three bounded probes were sufficient; no repeated query bottleneck justified an index. |
| Lifecycle pressure | Zombie create/remove/lookup remains a concrete list with no measured failure. |
| Behavior pressure | Only Zombie required local melee movement; Skeleton's ranged minimum-distance behavior stays separate. |
| Navigation pressure | A bounded side-step handled the tested obstacle. Global pathfinding is deferred. |
| ECS pressure | Nothing changes the Phase-E decision. The measured cost remains direct simulation plus replication, not proof for ECS. |

## Deferred / next pressure

Deferred: step-up/down semantics, slopes and partial collision shapes, global pathfinding, despawn,
generic AI, behavior trees, async navigation, shared mob base and ECS. The next highest-value
pressure is a second obstacle/terrain behavior that proves whether local movement remains enough;
only then should Skeleton be reconsidered for a shared world-query primitive.

The closure answer is: real mob behavior required one concrete target identity field and one bounded
world passability probe. The existing direct model remained sufficient.

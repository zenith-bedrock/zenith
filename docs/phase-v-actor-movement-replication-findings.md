# Phase V — Actor movement replication efficiency

## Scope

Phase V audited and changed only concrete movement projection in `ZombieSystem` and
`ProjectileSystem`. Gameplay still owns authoritative state and decides lifecycle; Protocol still
transmits outcomes; Packets serialize; RakNet transports. No movement component, replication
framework, ECS or entity hierarchy was introduced.

## Audit

- `EntityProtocol.SendMoveActorAbsoluteRaw` is a transport-facing projection of a pose; it does not
  decide whether a pose changed.
- Zombie and Projectile each maintain their own known-observer set through `ActorInterest`.
- AddActor/health and late-join paths remain separate from movement projection.
- RemoveActor clears the observer's known state and its last projected pose.
- Entering interest sends AddActor and initializes the projected pose before any movement decision.
- Explicit player teleport remains on the `MovePlayer` path and was not filtered by this change.

## Implemented concrete policy

Both concrete systems now retain `(actor, observer) -> last projected position`. After the
authoritative tick and interest reconciliation:

```text
authoritative position
        |
compare with last projected position
        |
meaningful delta? ---- no ---> skip MoveActorAbsolute
        |
       yes
        |
project movement and remember pose
```

The current threshold is a squared position delta of `0.0001` (approximately `0.01` blocks).
Spawn always initializes the state; remove and interest loss discard it. This is deliberately
duplicated in the two concrete systems rather than promoted to a generic movement/replication API.

The benchmark reports `moveFanout` and `moveSkipped` separately. The counters are runtime facts
owned by each concrete system and are not used to control gameplay.

## Tests

Added leaf coverage proves:

- Zombie does not emit a movement projection when it remains inside attack range and its
  authoritative position is unchanged.
- Projectile does not emit a movement projection when its authoritative position is unchanged.
- Existing interest crossing, spawn, removal and late-join behavior remains covered.

## Before/after evidence

Phase III's 1,000-Zombie / 10-observer direct reference was:

```text
avg tick 4.351 ms | 884 datagrams | 1,041,385 bytes | 23,800 movement fan-out
```

The same Debug runtime-load scenario after this change produced:

| workload | avg tick | allocations/tick | datagrams | bytes | movement sent | movement skipped |
|---|---:|---:|---:|---:|---:|---:|
| idle / 1,000 | 3.711 ms | 242,261 B | 140 | 93,692 | 0 | 0 |
| direct / 1,000 | 4.660 ms | 551,038 B | 884 | 1,041,106 | 23,791 | 9 |
| obstacle / 1,000 | 5.069 ms | 573,542 B | 923 | 1,105,610 | 25,804 | 11 |

The actor-churn baseline remains useful as a replication comparison. With 1,000 projectiles and
10 observers, the existing interest path measured `7.841 ms`, `2,755` datagrams and `3,557,152`
bytes, versus the global path's `50.385 ms`, `26,210` datagrams and `34,638,310` bytes. That is
interest management evidence, not movement-dirty evidence; projectiles moved materially each tick
and therefore correctly produced no skipped projections.

## Closure answers

1. **How much cost was avoidable?** Very little in the characterized Zombie workloads: 9 of 23,800
   direct projections and 11 of 25,815 obstacle projections. Idle already emitted no actor moves.
2. **Did it help the real workload?** No material improvement was demonstrated. The per-pair pose
   bookkeeping added a small simulation/allocation cost while avoiding a negligible number of
   packets. Egress was effectively unchanged.
3. **Is a shared movement primitive justified?** No. Two systems now prove the same invariant, but
   the benchmark does not show enough pressure to justify a public or generic primitive. The small
   concrete implementations remain easier to inspect.
4. **What remains the next pressure?** Observer fan-out and interest selection remain much larger
   than unchanged movement suppression. If a future actor has a stationary phase with large
   observer fan-out, remeasure before changing the policy or introducing batching/delta protocols.

## Decision

Keep the minimal concrete guard because it preserves correct spawn/remove/teleport boundaries and
eliminates redundant sends in stationary cases. Do not claim it as a capacity optimization yet.
Revisit or remove the bookkeeping if a production workload shows that its per-tick lookup cost is
greater than the avoided wire work. Do not create a shared movement component or replication
framework without materially stronger evidence.

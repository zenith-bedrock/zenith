# Phase VII — Actor replication pipeline and scaling foundation

## Scope and ownership audit

The current actor path is now explicit:

```text
Gameplay state
  -> ActorInterest decision
  -> concrete known-observer / last-pose tracking
  -> EntityProtocol projection
  -> packet serialization
  -> RakNet transport
```

Ownership remains deliberately concrete:

- `ZombieSystem` / `ProjectileSystem` decide actor existence, movement, health and lifecycle.
- `ActorInterest` decides only whether a living observer knows the actor's confirmed chunk.
- Each concrete system owns its `(actor, observer)` known set and last projected pose.
- `EntityProtocol` creates the wire projection; packets serialize it; RakNet sends it.
- No layer below Gameplay decides visibility, damage, actor lifetime or simulation.

AddActor, Health, RemoveActor and late-join/reconnect reconciliation remain separate from movement.
An observer entering interest receives AddActor and health; leaving interest receives RemoveActor;
movement batching cannot suppress either transition.

## Experiment: observer-local movement batching

Before this phase, every changed actor/observer pair called `SendMoveActorAbsoluteRaw` separately.
The smallest proven pipeline primitive was a protocol-only batch of raw non-player poses:

`RawActorPose` + `EntityProtocol.SendMoveActorAbsoluteRaws`.

Zombie and Projectile still decide each pose independently. They only collect changed poses for one
observer and submit one packet envelope when more than one pose is ready. This is not a
`ReplicationManager`, universal actor interface or generic pending-update queue.

The batching rule is:

```text
concrete gameplay system
  -> per-observer changed raw poses
  -> one EntityProtocol submission
  -> one RakNet send path
```

The existing movement dirty threshold remains in the concrete systems. `moveFanout` counts actor
projections; datagrams and bytes measure the transport result. The Diagnostics harness also records
`tick.system.zombie` for the scaling workload, while the benchmark prints projection and transport
facts without dynamic per-actor labels.

## Before / after — 1,000 actors × 10 observers

The same clustered/distributed/moving-observer workload was run before and after batching. Values
are Debug in-process GameLoop measurements; wall-clock p95 is noisy, so transport and allocation
changes are the stronger evidence.

| layout | metric | before Phase VII | after Phase VII |
|---|---|---:|---:|
| clustered | datagrams | 1,137 | 788 |
| clustered | bytes | 1,460,201 B | 1,003,363 B |
| clustered | allocations/tick | 1,318,806 B | 713,673 B |
| clustered | avg tick | 14.742 ms | 10.611 ms |
| distributed | datagrams | 400 | 400 |
| distributed | bytes | 84,405 B | 61,941 B |
| distributed | allocations/tick | 240,595 B | 173,245 B |
| moving observers | bytes | 102,611 B | 79,427 B |
| moving observers | allocations/tick | 261,463 B | 193,810 B |

Projection counts and interest transitions remained unchanged. Batching reduced envelope/transport
amplification, not the number of gameplay decisions.

## Scaling matrix

The harness supports:

```powershell
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --interest-scaling --actors 1000,5000,10000 --actor-players 10 --ticks 10
```

Representative results after batching (10 observers, 10 ticks):

| actors/layout | avg tick | allocations/tick | datagrams | bytes |
|---|---:|---:|---:|---:|
| 1,000 clustered | 11.367 ms | 693,023 B | 135 | 172,240 B |
| 1,000 distributed | 4.458 ms | 231,395 B | 10 | 12,542 B |
| 5,000 clustered | 87.321 ms | 3,399,804 B | 635 | 850,240 B |
| 5,000 distributed | 16.951 ms | 999,395 B | 10 | 12,542 B |
| 10,000 clustered | 267.690 ms | 6,797,864 B | 1,260 | 1,697,740 B |
| 10,000 distributed | 32.883 ms | 1,959,395 B | 10 | 12,542 B |

Observer scaling (2-tick stress samples) showed the same shape. At 10,000 clustered actors,
average tick was approximately `410.8 ms` with 50 observers and `581.7 ms` with 100 observers;
distributed was approximately `155.4 ms` and `313.9 ms`, respectively. These are pressure samples,
not capacity promises.

The dominant cost remains relevant observer fan-out and per-observer projection iteration. Batching
reduces packet envelopes and bytes, but it does not make 10,000 actors × many relevant observers a
20-TPS workload.

## Priority and ordering

The existing lifecycle ordering was preserved and tested:

1. AddActor and initial health establish observer knowledge.
2. Movement is lower priority and may be batched or skipped by the concrete dirty check.
3. Health changes remain direct authoritative updates.
4. RemoveActor clears known state and is never delayed by movement batching.

No QoS queue or priority scheduler was introduced. Current evidence proves ordering boundaries are
more important than a complex queue: batching movement does not reorder lifecycle or health.

## Accepted and rejected primitives

Accepted:

- `RawActorPose`, a wire-projection value for non-player actor movement;
- observer-local batching in `EntityProtocol`;
- concrete diagnostics counters and system timing in the existing benchmark harness.

Rejected:

- `ReplicationManager`, `EntityReplicationComponent`, `NetworkEntity`;
- universal actor interface or generic pending-update store;
- ECS, VisibilitySystem, spatial tree, MMO interest grid;
- QoS scheduler, parallel scheduler or networking abstraction.

The accepted primitive is permanent only because two concrete systems demonstrated the same
observer-local batching invariant and the before/after transport measurements were material.

## Bedrock/lifecycle coverage

Existing Zombie and Projectile tests continue to cover spawn, movement, health, removal, interest
crossing and late join. The Phase VI extension covers late join followed by disconnect/reconnect:
the observer receives RemoveActor while out of game and a fresh AddActor on re-entry without a
duplicate active actor or stale projection state. Raw batching has a leaf test proving two actor
poses occupy one transport frame.

## Closure decision

The smallest permanent primitive Zenith needs at this pressure point is observer-local batching of
concrete movement projections. It belongs at the Protocol projection boundary, not in Gameplay
state and not in a generic replication framework.

The next pressure is still relevant-observer fan-out. If future evidence shows that the per-observer
iteration itself dominates after batching, measure a concrete projection budget or actor activation
rule first. Do not infer that ECS, spatial indexing or a universal replication pipeline solves that
wire-dominated cost.

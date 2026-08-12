# Runtime and behavioral validation

Zenith uses two complementary checks. Neither is a replacement for the other.

| Check | Question answered | Tool |
| --- | --- | --- |
| Runtime baseline | Can this Zenith build keep its authoritative tick stable under a defined synthetic workload? | `zenith.Benchmarks --runtime-load` |
| Behavioral reference | Does a neutral external scenario produce the same semantic outcome on compatible server endpoints? | Dedicated client harnesses + JSONL observations |

The reference server is a behavior oracle, not an architectural target. Packet bytes, runtime IDs,
batching, and small timing differences are diagnostics. Authoritative outcomes such as spawning,
world mutation, inventory quantity, container content, health, and peer visibility are the values
that can establish a gameplay regression.

## Runtime baseline

Run from the Zenith checkout:

```bash
dotnet run --project src/zenith.Benchmarks -- --runtime-load
```

The default scales are 10, 100, and 500 synthetic in-game players. Each host uses the production
`GameLoop` registration order and calls its internal `TickOnce()` seam. This avoids wall-clock
sleep while still executing the real systems, protocol encoders, RakNet session flushing, and
authoritative inventory transactions.

`steady` submits one movement intent per player each tick and a valid, resource-conserving
inventory swap every ten ticks. The harness verifies after every tick that each player's dirt
quantity remains constant. It measures average/p50/p95/p99/max tick time, current-thread
allocation, GC collections, and captured RakNet egress.

At 20 TPS, the tick budget is **50 ms**. Treat p95/p99 below 50 ms as passing this baseline;
headroom and GC pressure are diagnostic concerns, not failures by themselves. Do not accidentally
use 20 ms as the budget — that would describe 50 TPS, not the Zenith runtime.

`chunk-burst` enables a small chunk radius for the same players and exercises the real async
column completion handoff. It is intentionally a burst measurement, not a claim that all columns
have completed after its short window.

Examples:

```bash
# Repeat the normal 10/100 baseline with a longer sample.
dotnet run --project src/zenith.Benchmarks -- --runtime-load --players 10,100 --ticks 100

# A short structural probe at the expensive 500-player scale.
dotnet run --project src/zenith.Benchmarks -- --runtime-load --players 500 --ticks 10
```

### Actor churn baseline (Phase D)

`actor-churn` is the corresponding dynamic-world-actor baseline. It uses the real
`ProjectileSystem`/`ProjectileStore`, production `GameLoop` order, protocol encoders and recording
RakNet transport; it is deliberately not a synthetic component/ECS benchmark. The default actor
scales are 100 and 1,000 projectiles, with ten in-game observers and 200 ticks. Projectiles use
their actual 80-tick lifetime, so a 200-tick sample contains removal and replenishment rather than
a one-time seeded population.

```bash
# Phase-D reproducible one- and ten-observer comparison.
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --actors 100,1000 --actor-players 10 --ticks 200
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --players 1 --actors 100,1000 --actor-players 1 --ticks 200
```

It reports the normal tick distribution, allocation and GC counters together with active/target
actors, spawn/move fan-out, removes, RakNet datagrams and bytes. The results intentionally combine
simulation and wire work: compare observers to expose fan-out pressure, but do not infer that ECS
is a visibility solution. The full 2026-08-11 results and cost decomposition are in
[`phase-d-actor-pressure.md`](phase-d-actor-pressure.md).

Do not compare these timings directly with another implementation. They are a reproducible
baseline for Zenith itself. In particular, the current architecture deliberately fans dirty
movement to all in-game peers; a 500-player global-movement run measures that known O(players²)
boundary. It is evidence for a future visibility requirement, not permission to introduce a
`VisibilitySystem` without a concrete feature/ADR.

## Reading a baseline

Keep capacity, allocation pressure, and the scaling model separate:

- **Performance capacity:** 20 TPS means a 50 ms tick budget. A workload can remain within that
  budget while still having undesirable allocation behavior.
- **Memory pressure:** allocation per tick and Gen0/Gen1/Gen2 collection counts show whether a
  short run is already creating promotion and GC-jitter risk. Treat frequent Gen2 collections as
  a finding even when p95 remains below 50 ms.
- **Scaling model:** dirty movement replicated to every peer is O(players²). A benchmark that
  moves every player every tick is deliberately exposing that boundary, not modelling typical
  player idleness or spatial visibility.

Interpret `chunk-burst` separately from `steady`. It begins real asynchronous column work and
then lets completions publish through the gameplay handoff. A short burst may have a low median
and a very high tail when several completions serialize in the same tick. That identifies burst
concentration; it is not evidence that every normal chunk-stream tick costs the reported maximum.

## Behavioral reference runs

The external [`zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) remains the
real-client regression harness for Zenith's curated starter world. It must not acquire
reference-server workarounds merely to make a comparison pass. Keep a compatibility probe separate
when another server needs different login or transport details.

Before calling any comparison a gameplay result, establish all three conditions:

1. Both endpoints use a matching packet **schema**, not merely a matching wire protocol number.
2. The scenario prepares its initial state through client-observable actions, rather than relying
   on a Zenith-specific seed, starter inventory, or internal ID.
3. The assertion is semantic: spawn, visible peer, block state, inventory quantity, container
   content, health, or another authoritative outcome. Packet bytes, runtime IDs, batching, and
   small timing differences are diagnostic only.

An overridden wire ID does not make an older packet schema compatible with a newer server. It can
still be useful to prove RakNet reachability and a narrow join/spawn lifecycle, but any packet
decode error or later divergence is **inconclusive**. Do not change Zenith from that result.

Write one normalized JSON or JSONL observation per participant and preserve the raw client log.
For a confirmed difference, create a minimal reproduction, decide whether it is a Zenith bug,
reference-specific behavior, intended Zenith policy, or a harness fault, then add a Zenith-side
regression test before making the smallest justified correction.

The target-specific Zenith smokes remain useful for Zenith's curated starter world. Do not use
those scenarios as behavioral reference tests until their setup is neutral for both endpoints.

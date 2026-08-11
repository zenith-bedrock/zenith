# Runtime and behavioral validation

Zenith uses two complementary checks. Neither is a replacement for the other.

| Check | Question answered | Tool |
| --- | --- | --- |
| Runtime baseline | Can this Zenith build keep its authoritative tick stable under a defined synthetic workload? | `zenith.Benchmarks --runtime-load` |
| Differential smoke | Does the same external Bedrock client observe the same semantic result from Zenith and a reference server? | `zenith-smoke-bot` |

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

## Differential smoke

The external [`zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) remains the
real-client harness. Its neutral first scenario compares join/spawn against two endpoints:

```bash
DIFF_ZENITH_PORT=19135 \
DIFF_REFERENCE_PORT=19137 \
DIFF_REFERENCE_PROTOCOL_VERSION=2168 \
bun run smoke:differential-join
```

The output is a normalized observation for each target. Both targets must spawn before the result
is called equivalent. A protocol/schema or RakNet handshake failure is **inconclusive**, not a
Zenith gameplay failure. Before changing the server, reproduce the observable difference, classify
it as semantic or transport/harness-specific, and add a server-side regression test when Zenith is
actually at fault.

The target-specific Zenith smokes remain useful for Zenith's curated starter world. Do not use
those scenarios as differential tests until their setup is made observable and neutral for both
servers.

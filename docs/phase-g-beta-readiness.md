# Phase G — beta readiness foundation

## Operational contract

Zenith remains a single-writer 20 TPS server. The operational tick budget is **50 ms**;
measurements below are local reproducible envelopes, not a public MMO capacity claim.

## Capacity baseline

Run the production-order harness:

```bash
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --players 1 --actors 1000 --actor-players 10 --ticks 200
```

The Phase-F controlled 1,000 Projectile / 10 observer / 200 tick result was:

| Interest mode | avg tick | p95 | allocation/tick | datagrams | bytes | movement fan-out |
|---|---:|---:|---:|---:|---:|---:|
| global | 53.564 ms | 71.020 ms | 11,407,529 B | 51,780 | 68,851,390 B | 1,980,000 |
| chunk knowledge | 8.601 ms | 12.165 ms | 1,262,316 B | 5,357 | 6,979,360 B | 198,000 |

Therefore 1,000 active short-lived actors with one relevant observer is inside the tick budget in
this harness; global replication to ten observers is not. The configured player maximum remains
`server.max-players` (default 20), not a load-tested promise. Chunk generation, retained memory,
real UDP loss and public-Xbox authentication require separate release evidence.

## Runtime signals

Every 30 seconds the server logs one compact `runtime:` line with tick duration, over-budget
count, connected players, concrete active actors, RakNet session count, inbound/outbound datagrams
and bytes, GC collections/managed bytes, and the last graceful persistence flush duration. These
are diagnostic signals, not a metrics endpoint or a new runtime layer.

## Automated Bedrock E2E

[`bedrock-e2e.yml`](../.github/workflows/bedrock-e2e.yml) builds Release, starts Zenith, checks
out the separate ADR §58 Bun client, then runs login/spawn, block/inventory interaction, actor
Zombie/Projectile lifecycle and reconnect smokes. The bot remains deliberately outside this C#
repository.

## Recovery evidence and boundaries

Existing lifecycle tests prove idempotent shutdown and concurrent startup/game-loop failure
cleanup. `SlotBlobPersistTests` proves LevelDB flush waits for inventory writes; `WorldTests`
proves overlays survive a fresh World instance. The Bedrock bot's `smoke:persist` is the
end-to-end restart exercise when LevelDB is configured.

Hard kill, disk-full and arbitrary LevelDB corruption are not claimed safe: WAL may preserve data,
but recent asynchronous writes are not guaranteed. The server logs a timed-out or failed graceful
flush explicitly and does not silently swap persistent storage for memory.

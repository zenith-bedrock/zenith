# ADR §111 — World generation is a bounded, shared pipeline

## Context

World generation currently has two different costs that were being treated as one:
the asynchronous production of a column and the synchronous consumption/transmission of
completed columns by `ChunkStreamSystem`. A client run showed the consequence: 737 columns
were generated, `gameplay.worldgen.column.payload` accumulated about 118 seconds, and
`tick.system.chunk-stream` accumulated about 55 seconds with a 1.36 second maximum sample.
The worker-side generation did not protect the GameLoop from CPU contention or from draining
too many completed payloads in one tick.

The local PocketMine and Dragonfly references expose a second, more important mismatch in the
old Zenith flow. PocketMine's player requests a small number of chunks per tick, orders them from
the centre outwards, and calls `notifyTerrainReady` after a central spawn threshold; the rest of
the view keeps loading after spawn. Dragonfly's `Loader` applies the same centre-out queue and
loads at most the requested number per session tick, while its world request table deduplicates
generation and its chunk cache deduplicates compressed packets. Neither implementation waits for
the entire view radius before releasing the player.

Dragonfly's relevant design is concrete rather than generic: a world owns a deduplicated
chunk-request table and a fixed worker pool (with a conservative default in the current
reference), while completion is handed back to the world owner. This is applicable to
Zenith's single-writer model. The worker pool is not a general scheduler and does not mutate
gameplay state.

## Decision

Zenith world generation evolves into a bounded pipeline with four explicit stages:

```text
player interest
    -> shared request broker (deduplicate by chunk coordinate)
    -> bounded generation workers (immutable result only)
    -> gameplay-owned publication queue (per session, budgeted per tick)
    -> Protocol/RakNet transmission
```

- A coordinate has at most one active generation request. Additional consumers await the same
  immutable result; cancelling one wait never cancels the shared work. The broker dedups on
  `ChunkCoord` (X/Z) alone — there is exactly one `World` (and therefore one dimension, one
  generator, one seed) per server process today, so a coordinate is already an unambiguous key.
  This is **not** yet a `(world, dimension, generatorVersion, chunk)` key: no `generatorVersion`
  concept exists anywhere in the codebase (a known future gate, already called out in ADR §108's
  own non-goals). Widening the dedup key is real future work if/when Zenith supports multiple
  concurrent worlds or dimensions, or a versioned generator — not implemented now, and this
  paragraph should not be read as claiming otherwise.
- Generation concurrency is explicit and configurable. The default is four workers: still a fixed
  bounded pool, while avoiding a single-column queue on the join path. The scale harness measured
  radius 2 at about 2.25 s with one worker versus 0.84 s with four on the current host. Increasing
  it further remains an operational choice, not an automatic `ProcessorCount` decision.
- The broker owns only request lifecycle and immutable results. It does not write `World`,
  `Player`, overlays, sessions, or protocol state.
- The GameLoop consumes completed results and publishes them through a bounded per-player budget.
  It never waits for generation/I/O and never drains an unbounded completion queue in one tick.
- Spawn readiness is a central-disk barrier, not completion of the view radius. The view stream is
  ordered centre-outwards; once the configured ready disk has been transmitted, `SpawnComplete` is
  emitted and the remaining view continues through the session queue.
- Post-spawn world columns enter a bounded queue owned by `NetworkSession`; the session worker
  performs LevelChunk packet encoding/compression and sends on the dedicated world-stream channel.
  The GameLoop only decides/enqueues the already-authoritative result. Queue saturation abandons
  that stream slot for a later retry and is diagnosed; it cannot stall the tick.
- A generated payload may be shared between consumers, but publication and session transport
  remain per consumer. Cache lifetime and bounded column retention are explicit; payload bytes
  are measured diagnostically and there is no unbounded global cache.
- Diagnostics measure request, queue, generation, materialization, publication, sharing,
  dropping and backpressure as fixed low-cardinality metrics.

## Why this is an architectural change

Changing `Max...PerTick` alone would hide one symptom. The pipeline separates the ownership and
budgets that were previously implicit: CPU work has a worker budget, result retention has a byte
budget, and network publication has a per-tick budget. Each boundary has a testable invariant.

## Invariants

1. For the same chunk coordinate, active generation count is one, except for an explicitly counted
   retry after failure. (Single `World`/dimension/generator per process today — see the Decision
   section's note on why `ChunkCoord` alone is currently an unambiguous key.)
2. A cancelled/disconnected consumer removes only its wait/publication; it cannot cancel or corrupt
   work still needed by another consumer.
3. No generation, storage wait, payload encoding or unbounded publication drain runs on the
   GameLoop thread.
4. A failed request is removed from the active table and cannot leave a permanent in-flight entry.
5. Every published payload is byte-equivalent to the deterministic generator result for its key.
6. Queue depth, retained columns, in-flight requests and per-tick publication are bounded and
   visible in `Zenith.Diagnostics`; payload bytes are accounted for separately.

## Consequences

The default cold radius may complete more slowly than unconstrained CPU fan-out, but it no longer
turns generation bursts into tick stalls. Throughput can be increased deliberately after measuring
the host's tick headroom. The existing point-sampling path remains available for isolated calls;
the pipeline path is the canonical path for chunk generation and multi-player overlap.

## Benchmark evidence (post-implementation, `--worldgen-scale` harness, this host)

Recorded when closing out the audit that reviewed this ADR's implementation — the mask optimization
(§ below) had shipped without a measured before/after; this fills that gap.

| Scenario | radius | players | workers | elapsedMs | payloadAvgMs | cavesAvgMs | coalesced |
|---|---|---|---|---|---|---|---|
| single player | 2 | 1 | 4 | 240.53 | 29.33 | 1.21 | 0 |
| single player | 4 | 1 | 4 | 329.88 | 13.10 | 0.52 | 0 |
| multiplayer | 2 | 4 | 4 | 110.55 | 8.41 | 1.19 | 75 |
| multiplayer | 2 | 16 | 4 | 63.90 | 5.57 | 0.73 | 375 |

**Cave column-mask isolated (radius 2/4, 1 player, 4 workers, mask lookup disabled vs. enabled,
same host):**

| | payloadAvgMs (mask off) | payloadAvgMs (mask on) | speedup |
|---|---|---|---|
| radius 2 | 86.96 | 29.33 | ~3.0× |
| radius 4 | 37.28 | 13.10 | ~2.8× |

The per-column cave-context build cost itself (`cavesAvgMs`, i.e. `PrepareColumnMask`) is
unaffected either way (~1.2ms / ~0.5ms) — the win is entirely in the voxel sampling loop, which
previously paid a segment-intersection query per candidate voxel and now does an O(1) bit lookup for
every voxel in the column's central chunk. Confirms the mask closes the intended gap rather than
just adding allocation overhead for no benefit.

**Radius-256 (263,169 columns) timeout safety, 1 player, 4 workers, 120s budget:** completed
209,824/263,169 columns before the configured timeout, status `timeout-or-cancelled` (not a crash or
OOM), queue peak stayed at the same bound (8) as the small-radius runs. Confirms the queue-depth
invariant holds under sustained backpressure, not just at small scale.

## References

- `D:\Development\bedrock\dragonfly\server\world\chunk_request.go` — deduplicated request and
  fixed worker-pool design.
- `docs/plans/worldgen-deterministic-data-oriented.md` — benchmark matrix and diagnostics contract.
- `ARCHITECTURE.md` — single-writer ownership and decide/transmit/serialize boundaries.

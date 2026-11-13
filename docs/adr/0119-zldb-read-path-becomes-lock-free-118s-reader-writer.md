# ADR §119 — ZLDB read path becomes lock-free — §118's reader/writer lock didn't deliver, benchmarks proved it

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §119`.

**Choice:** At the user's explicit request to dig deeper — "gaps, performance problems from a
dedicated benchmark, concurrency, multi-threading" — added a real `BenchmarkDotNet` benchmark
project (`src/zenith.Benchmarks/LevelDbBenchmarks.cs`,
`LevelDbConcurrencyBenchmarks`, `LevelDbLockPrimitiveBenchmarks`) instead of reasoning about
locking from code review alone. The result reversed §118's own conclusion.

**What the benchmark found:** §118's `ReaderWriterLockSlim` does **not** deliver the "concurrent
reads stop blocking each other" benefit it was introduced for. Measured with 8 threads each doing
20,000 `TryGet` calls on the same `DB`:

| Threads | §118 (RWLS) total time | Throughput |
|---|---|---|
| 1 | 709.5 us | 28.2M ops/s |
| 2 | 1,412.3 us | 28.3M ops/s |
| 4 | 3,238.7 us | 24.7M ops/s |
| 8 | 8,513.6 us | 18.8M ops/s |

Total throughput **fell** as concurrency rose — the opposite of the intended effect. Isolated the
cause with a dedicated comparison (`LevelDbLockPrimitiveBenchmarks`, same read-only
`SortedDictionary` lookup guarded three ways): both a plain mutex and `ReaderWriterLockSlim` were
**18-20x slower than no lock at all** at 8 threads. Distinct-keys-per-thread and same-key variants
scaled identically, ruling out cache-line contention on shared tree nodes — the bottleneck was the
lock's own acquire/release bookkeeping, not the data being protected. `ReaderWriterLockSlim` was
also measurably *worse* than a plain mutex at low contention (1 thread: 43% slower) because its
internal state tracking costs more per acquisition even uncontended — it only ever broke even with
a plain mutex, never clearly won, across every thread count tested.

**Fix: eliminate the read-side lock, not swap the primitive.** `MemTable` (`MemTable.cs`) now holds
an `ImmutableSortedDictionary<byte[], byte[]?>` behind a single field, published via
`Volatile.Write` after each mutation — never mutated in place. A reader does a plain
`Volatile.Read` of the current reference and operates on that snapshot; an immutable collection
can't be observed half-updated, so `TryGet`/`GetSnapshot`/`CreateIterator` need **no lock at all**.
`DB`'s lock (`_writeGate`, a plain `object`) now exists purely to serialize `Write`/`Close`/flush —
necessary because WAL append and memtable mutation must stay atomic together for durability, not
because the dictionary itself needs protecting from readers anymore. `_closed` became `volatile
bool` so lock-free readers still observe a `Close()` on another thread correctly.

**Result, same benchmark, after the fix:**

| Threads | Before (RWLS) | After (lock-free) | Speedup | Throughput after |
|---|---|---|---|---|
| 1 | 709.5 us | 548.2 us | 23% faster | 36.5M ops/s |
| 2 | 1,412.3 us | 646.4 us | 54% faster | 61.9M ops/s |
| 4 | 3,238.7 us | 707.7 us | 78% faster | 113.0M ops/s |
| 8 | 8,513.6 us | 784.9 us | 91% faster | 203.8M ops/s (**10.8x** §118's number) |

Throughput now scales roughly with thread count instead of degrading — genuine parallelism, not
just "no deadlock." Single-threaded `TryGet` remains ~100-130ns, 0 bytes allocated
(`LevelDbBenchmarks`). `Put` got measurably more expensive per call (~7.4us, some allocation) —
`ImmutableSortedDictionary.SetItem` path-copies on every write, versus a mutable dictionary's
cheaper in-place insert. Accepted deliberately: writes are already single-writer/serialized by
`_writeGate` regardless, ZLDB's actual write pattern is sparse per-tick overlay edits (not
write-heavy bulk load), and the entire point of this pass was fixing the read path.

**Empirical validation, not just benchmark numbers:** added two concurrency stress tests
(`libs/leveldb.Tests`) that exercise the new lock-free design directly rather than trusting the
reasoning alone — many threads doing concurrent `Put`+`CreateIterator`+`TryGet` against distinct
keys (validates no lost writes, no corruption, no deadlock: final count must exactly match
`writerCount * writesPerWriter`), and a same-hot-key overwrite race where every concurrent read
must observe one complete, self-consistent version of the value or none — never a torn mix of an
old and a new write's bytes (validates the copy-on-write safety claim `TryGet` depends on). Both
green across repeated runs.

**Two unrelated pre-existing bugs found and fixed while investigating, not caused by this pass:**
1. `Options.OnCorruption` was `public static` on `DB` — wrong for a class that can legitimately
   have more than one instance open at once (multiple worlds, or `Repair` opening one internally):
   setting it on one instance silently affected every `DB` in the process, and only ever fired for
   whichever instance happened to still be replaying its WAL when set. Moved to
   `Options.OnCorruption` (per-instance, read once during that instance's `Open`).
2. A genuinely flaky test unrelated to ZLDB
   (`WorldGenerationDiagnosticsTests.World_coalesces_concurrent_column_generation_without_sharing_cancellation`)
   surfaced under the heavy thread-pool load these benchmarks/stress tests generate. Root cause:
   the test raced `secondCancellation.Cancel()` against `terrain.Release.Set()`, assuming
   cancellation would always "win" — but `World.GetOrCreateColumnAsync`'s
   `request.Task.WaitAsync(ct)` resolves the canceled branch independently of the shared
   generation completing, so under scheduler pressure the two could reorder and the second caller
   would observe the coalesced generation's successful result instead of its own cancellation.
   Fixed by waiting for `second.IsCompleted` (which `WaitAsync`'s cancellation path satisfies
   without needing the terrain released) *before* releasing the terrain, removing the race instead
   of hoping for a favorable order. Separately, found the wider cause class: `Tools`/`Blocks`/etc.
   are process-wide statics with a locked mutation path but a lock-free fast-read path once loaded
   — safe within one test class, not safe against a *different* xUnit test class concurrently
   resetting/reloading the same static state, which xUnit runs in parallel by default. Added
   `src/zenith.Tests/xunit.runner.json` (`parallelizeTestCollections: false`) rather than
   redesigning every static loader — a deliberate test-infra trade (slower local/CI runs, ~1m20s →
   ~2m for the full suite, still well inside the CI timeout) over a much larger, riskier product
   change to fix a test-only symptom.

   **Superseded 2026-11-13:** parallelism is back on (`parallelizeTestCollections: true`). The cause
   was a real publication race, not just a test artifact: `Tools`/`BlockLoot` cleared and refilled
   plain dictionaries in place on `Load`, and `Blocks` assigned its fields one by one. All three now
   build an immutable generation off to the side and publish it with one `volatile` reference, so the
   `ResetForTests` hooks were deleted. Tests that mutate process-wide env/ports or assert wall-clock
   timing opt out with `[Collection(ExclusiveCollection.Name)]`.

**Why now, not before:** direct continuation of §115-118 at the user's request to keep going
deeper — benchmarks and concurrency stress tests are the natural next step after §118 shipped a
locking change that turned out, once measured, not to be the right one.

**Non-goals reaffirmed:** multi-writer lock-freedom (still single-writer via `_writeGate`, by
design — WAL/memtable atomicity needs it); compaction/multi-level LSM/block cache/Bloom filters —
still §11/§61.

**Status (ago 2026):** Shipped — `MemTable.cs` rewritten (`ImmutableSortedDictionary` + atomic
swap), `DB.cs` lock model simplified to write-only exclusion, `Options.OnCorruption`. New:
`src/zenith.Benchmarks/LevelDbBenchmarks.cs`, `LevelDbLockPrimitiveBenchmarks.cs`; two new
concurrency stress tests in `libs/leveldb.Tests` (24 cases total, up from 22). Collateral fixes:
`WorldGenerationDiagnosticsTests.cs` race condition, `src/zenith.Tests/xunit.runner.json`. Full
`zenith.sln` build and 1019-case `zenith.Tests` run green, repeated multiple times for stability.

# ADR §120 — `Seek` becomes O(log n), `Destroy`/`Repair` refuse when locked, extensive edge-case coverage

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §120`.

**Choice:** Continued the deep-dive at the user's explicit request to keep finding architecture
gaps and fix them, with "extensive tests covering every minimal detail and complex case." Full
re-read of every file in `libs/leveldb/` surfaced two real gaps beyond what §115-119 covered, both
fixed, plus a large new test file.

1. **`Iterator.Seek` was O(n), not O(log n).** `Snapshot`/`Iterator` were backed by
   `SortedDictionary<byte[],byte[]>`, and `Seek` walked it with a linear `while (_enum.MoveNext())`
   scan comparing every key until finding the target — `SortedDictionary` exposes no
   binary-searchable view (unlike `SortedSet<T>.GetViewBetween`), so there was no way to seek
   faster than a full scan through that type's public API. Separately, `DB.GetSnapshot` built that
   `SortedDictionary` by re-inserting each of `MemTable.LiveEntries()`'s entries one at a time
   (O(n log n)) — wasted work, since `LiveEntries()` already returns entries in `ByteComparer`
   order (it iterates the already-sorted `ImmutableSortedDictionary` §119 gave `MemTable`). Fixed
   both by dropping `SortedDictionary` entirely: `Snapshot`/`Iterator` now hold the sorted
   `IReadOnlyList<KeyValuePair<byte[],byte[]>>` `LiveEntries()` already produces, and a shared
   `SortedEntrySearch.LowerBound` binary-searches it directly for both `Snapshot.TryGet` and
   `Iterator.Seek`. `GetSnapshot` is now a direct O(n) copy, `Seek` is O(log n).
2. **`Destroy`/`Repair` could silently corrupt an actively-open DB.** Neither checked the `LOCK`
   file before acting. `Repair` opening its own tolerant `DB` instance already inherited §117's
   lock check and would correctly fail if another instance held the directory open, but `Destroy`
   went straight to best-effort per-file deletes — if another process/instance had the directory
   open, those deletes would mostly fail with sharing violations that `TryDeleteBestEffort`
   silently swallows, but not *uniformly*: `CURRENT`/orphan `.ldb` files (not held open long-term)
   could still be deleted successfully while `LOCK`/the active journal file failed, leaving the
   other instance's directory in a half-destroyed state while it kept running, with the caller
   believing `Destroy` had simply succeeded. Fixed: `Destroy` now probes `LOCK` the same way
   `Open()` acquires it, before touching anything, and throws `InvalidOperationException` if
   another instance holds it — refusing cleanly instead of silently partially succeeding.
3. **Extensive new test file** (`libs/leveldb.Tests/LevelDbEdgeCaseTests.cs`, 66 new cases,
   `libs/leveldb.Tests` total 22 → 66): `Seek`/binary-search correctness (exact match at every
   position, landing between two keys, before-first, past-last, empty snapshot, a 500-entry
   shuffled-insert full-iteration ordering check), `Destroy`/`Repair` refusing-when-locked (and
   `Destroy` succeeding once the lock is released), empty key/value round-trip, a 2MB value
   round-tripping through both WAL and a flushed snapshot, same-key put/delete churn surviving
   WAL replay to the exact last write, tombstone lifecycle (`DropTombstones` actually shrinks the
   live set and deleted keys don't resurrect; `GetSnapshot` excludes tombstones even pre-flush),
   multiple `Iterator`s over one `Snapshot` used concurrently from different threads, concurrent
   `Close()` from multiple threads (idempotent, lock released exactly once), `WriteBatch`
   same-key multi-op last-write-wins (matching the class's own documented example), and direct
   unit tests of `MemTable`/`ByteComparer`/`Crc32`/`KvFraming` in isolation via
   `InternalsVisibleTo` (`Crc32` checked against the well-known CRC-32/ISO-HDLC test vector for
   ASCII "123456789" — 0xCBF43926, matching zlib/PKZIP/`System.IO.Hashing.Crc32`).

**Two more test-infra bugs found and fixed while writing that suite, not caused by this pass:**
1. `libs/leveldb.Tests` was missing the `xunit.runner.json`
   (`parallelizeTestCollections: false`) §119 added to `src/zenith.Tests` for the exact same
   reason — `DB.AfterTableWriteBeforeCurrent`, a static test-only fault-injection hook, leaked
   from one test class into another running concurrently in a different xUnit collection,
   surfacing as a spurious `IOException` in an unrelated new test. Same fix applied here.
2. A genuine arithmetic mistake in a newly-written test
   (`Same_key_put_delete_churn_survives_crash_and_replay_to_the_last_write`): the original loop
   ran 50 iterations claiming "the last op is a Put" because "49 % 7 != 0" — but 49 = 7×7, so
   49 % 7 *is* 0, meaning the actual last op was a `Delete`, not a `Put(49)`. Fixed by using 49
   iterations (last index 48, 48 % 7 == 6) so the asserted final state matches the code's actual
   behavior. Caught by the test itself failing, not by review — exactly the kind of complex case
   the user asked this pass to cover.

**Why now:** direct continuation of §115-119 at the user's explicit request — "identify gaps and
architecture problems and fix them, extensive tests covering every minimal detail and complex
case."

**Non-goals reaffirmed:** no change to the CRC algorithm, WAL/snapshot format, or lock model —
this entry is algorithmic (Seek) and safety (Destroy) hardening within the shape §115-119 already
established, plus test depth, not a new design direction.

**Status (ago 2026):** Shipped — `Snapshot.cs`/`Iterator.cs` rewritten (sorted-list + binary
search), `DB.cs` (`GetSnapshot` simplified, `Destroy` lock check), new
`libs/leveldb.Tests/LevelDbEdgeCaseTests.cs`, `libs/leveldb.Tests/xunit.runner.json`. All 66
`libs/leveldb.Tests` cases green (repeated 3x for stability), full `zenith.sln` build and
1019-case `zenith.Tests` run green.

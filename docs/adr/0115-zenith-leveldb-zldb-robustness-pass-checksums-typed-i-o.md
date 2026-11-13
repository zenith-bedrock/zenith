# ADR §115 — Zenith.LevelDB (ZLDB) robustness pass — checksums, typed I/O exceptions, reusable snapshots

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §115`.

**Choice:** Harden ZLDB within its existing, already-decided shape (§11: RAM `SortedDictionary` +
WAL + one snapshot, no compaction/multi-level LSM) rather than move toward a fuller LSM engine.
Compared `libs/leveldb/*.cs` against `mojang-leveldb` (Mojang's real C++ LevelDB fork, cloned
locally) specifically for correctness/robustness technique, not to import compaction/block-cache/
Bloom-filter machinery — those remain non-goals per §11/§61, not revisited here. Four changes:

1. **WAL checksums** (`Crc32.cs`, `Journal.cs`). Every record gets a
   `type:u8 | crc:u32 | bodyLen:u32 | body` envelope; `crc` covers type+body, not `bodyLen` —
   mirroring real LevelDB's WAL record checksum strategy (CRC32C over type+payload, length field
   excluded), using plain CRC-32 instead of CRC32C/Castagnoli since ZLDB's format is already
   explicitly non-compatible with Mojang/RocksDB (no reader needs the same polynomial). Replay now
   distinguishes a torn tail (crash mid-append — silent EOF, as before) from a structurally-complete
   record whose CRC doesn't match (genuine corruption — stops replay there, reports via the new
   `DB.OnCorruption` hook, doesn't trust anything after it). This closes the exact gap the README's
   own "Optional later (not P1)" list had named ("WAL framing + CRC (torn last record)") — not new
   scope, executing a debt the library already flagged on itself.
2. **Snapshot checksum** (`TableFile.cs`). One trailing CRC-32 over the whole `.ldb` payload
   (built in RAM first, consistent with ZLDB's existing "entire dataset fits in RAM" constraint —
   unlike real LevelDB's per-block trailers, which exist specifically to avoid buffering an entire
   multi-file SSTable, not a concern here). A magic-byte mismatch still means "foreign/stale file,
   treat as empty" (unchanged); a magic match with a bad CRC now throws `InvalidDataException`
   instead of silently loading (or discarding) corrupted product data.
3. **Typed I/O exceptions** (`LevelDbIOException.cs`, an `IOException` subtype — existing
   `catch (IOException)` call sites, including the mid-flush crash test, keep working unchanged).
   Wraps raw `IOException`/`UnauthorizedAccessException` from the WAL append/flush path (`Write`)
   and the snapshot write / `CURRENT` publish path (`FlushSnapshotUnlocked`), preserving the
   original as `InnerException`. Logical format errors (bad `CURRENT`, corrupted snapshot, bad
   batch encoding) keep their existing `InvalidOperationException`/`InvalidDataException` — not
   wrapped, already clear.
4. **Reusable `Snapshot`** (`Snapshot.cs`). `CreateIterator()` already built a consistent
   point-in-time copy of the dataset per call (real isolation, confirmed correct) but recreated
   that O(n) copy on every call with no way to reuse one materialized view for several point reads.
   `DB.GetSnapshot()` now returns that same copy as a named, reusable `Snapshot` (`Get`/
   `CreateIterator`); `CreateIterator()` is now a thin wrapper over it. Not real MVCC (no sequence
   numbers, no tombstone filtering, unlike real LevelDB's `GetSnapshot`/`SequenceNumber` mechanism)
   — a full copy-per-snapshot is a proportionate, much simpler equivalent given ZLDB's RAM-resident
   scope.

**Why now, not a bigger rewrite:** the trigger was investigating "resident chunk state" (evicting
World's overlay/chest RAM by chunk), which surfaced that ZLDB's on-disk overlay/chest keys are
per-block, not chunk-prefixed — a genuine, separate, larger question deferred back to §114's scope.
That investigation is what surfaced these more urgent, better-scoped ZLDB gaps in the process. None
of the four changes here touch key schema, compaction, or `World`/`IChunkStorage` — pure hardening
of the storage engine's own already-decided shape.

**Non-goals reaffirmed, not revisited:** compaction, multi-level LSM, block cache, Bloom filters
(§11/§61); directory-level fsync after the `CURRENT` rename (confirmed real LevelDB doesn't do this
either — an accepted upstream risk, not a ZLDB-specific gap); sequence-numbered MVCC.

**Status (ago 2026):** Shipped — `Crc32.cs`, `LevelDbIOException.cs`, `Snapshot.cs`, `Journal.cs`/
`TableFile.cs`/`DB.cs` updated. `libs/leveldb.Tests` grew from 11 to 15 cases (WAL mid-stream
corruption + `OnCorruption` reporting, corrupted-snapshot rejection, snapshot isolation,
`LevelDbIOException` contract); all 15 green, full `zenith.sln` build/test unaffected.

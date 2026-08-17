# Zenith.LevelDB

Managed key/value store for Zenith chunk overlay storage (`c:` / `ov:` keys). Own on-disk format (ZLDB) — **not** binary-compatible with `LevelDB.Standard`, Mojang Bedrock worlds, or RocksDB.

**Product stance (ADR §61):** ZLDB stays the **default** Zenith world engine. Opening Mojang/BDS folders or converting them into Zenith keys is a separate `IChunkStorage` backend + offline converter — not a rewrite of this library into a full zlib LSM.

## Constraint (read this first)

**The entire dataset must fit in RAM while the database is open.**  
On open, the snapshot is loaded into an in-RAM structure that is the runtime source of truth; Get/Iterator never stream cold SSTs from disk. That is a conscious trade for Zenith’s sparse `c:`/`ov:` overlays in a single process — not “we forgot Bloom filters.” If you need datasets larger than host memory, this store is the wrong tool (revisit streaming/LSM then).

Recreate `world.path` after format changes; no migration from NuGet LevelDB dirs.

**One `DB` instance per directory, enforced.** `Open` takes a real OS-level exclusive lock on
`LOCK` (`FileShare.None`, held for the DB's lifetime — mirrors real LevelDB's `LockFile`), not just
a marker file. A second `new DB(...)` on the same directory — same process or a different one —
throws `InvalidOperationException` immediately rather than silently running two independent
in-RAM copies that would clobber each other's WAL/snapshot on flush. `Destroy`/`Repair` (below)
probe the same lock first and refuse (throw) if another instance holds it, rather than silently
best-effort-deleting files out from under a directory someone else still has open (ADR §120).

**Reads take no lock at all (ADR §119).** §118 first tried a `ReaderWriterLockSlim` so concurrent
reads would stop blocking each other — dedicated benchmarks then showed that was the wrong fix: for
ZLDB's real workload (many short, cheap point reads), *any* lock — a plain mutex or
`ReaderWriterLockSlim` alike — cost 18-20x throughput at 8 concurrent readers versus no lock,
because the lock's own bookkeeping dominated. `MemTable` is now lock-free: an
`ImmutableSortedDictionary` behind a single field, swapped atomically after each write, never
mutated in place. `TryGet`/`CreateIterator`/`GetSnapshot` just read the current reference — no lock
to wait on. `Write`/`Close`/the flush they may trigger still serialize on a plain mutex, which
exists to keep WAL append and memtable mutation atomic together, not to protect the dictionary from
readers. Measured result: 8 concurrent readers went from *slower* than 1 (§118) to a real 10.8x
throughput improvement — see ADR §119 for the full before/after numbers and the benchmark code in
`src/zenith.Benchmarks/LevelDbConcurrencyBenchmarks`/`LevelDbLockPrimitiveBenchmarks`.

**`Iterator.Seek` is O(log n), not O(n) (ADR §120).** `Snapshot`/`Iterator` are backed by a plain
sorted list, not a `SortedDictionary` — `MemTable.LiveEntries()` already returns entries in
`ByteComparer` order (it iterates the already-sorted `ImmutableSortedDictionary` backing
`MemTable`), so `GetSnapshot` is now a direct O(n) copy instead of re-inserting each entry into a
fresh `SortedDictionary` (O(n log n)), and `Seek`/`TryGet` binary-search the sorted list directly
instead of linearly scanning from the start — `SortedDictionary` has no API for seeking to a
position without a full scan, which is what motivated the switch.

**A missing `CURRENT` target now fails loudly if it looks like our own file (ADR §121).** Two
different problems used to look identical — "`CURRENT` names a file that's missing." A name shaped
like ZLDB's own `{n:D6}.ldb` convention but absent from disk now throws `InvalidDataException`
(same treatment as a CRC-mismatched snapshot, §115) — that file should exist and doesn't, which
looks like real data loss, not foreign residue. A name that *isn't* shaped like a ZLDB table (e.g.
a leftover native LevelDB `MANIFEST-XXXXXX`) still resets empty silently, exactly as before — that
directory was genuinely never ours. `Repair` tolerates both cases, same as it already tolerated a
corrupted snapshot.

## API

```csharp
using var db = new DB(new Options { CreateIfMissing = true }, path);

db.Put(key, value);
db.Delete(key);

if (db.TryGet(key, out ReadOnlyMemory<byte> value)) { /* zero-alloc — no clone (ADR §118) */ }

var batch = new WriteBatch();
batch.Put(keyA, valueA);
batch.Delete(keyB);
db.Write(batch, new WriteOptions { Sync = true }); // fsync WAL

using var it = db.CreateIterator();
it.Seek(prefix); // O(log n) binary search (ADR §120), not a linear scan
while (it.IsValid()) { /* it.Key()/it.Value() are ReadOnlyMemory<byte> */ it.Next(); }

// Want several consistent point reads/iterations against one point-in-time view instead of
// paying the copy CreateIterator() does on every call? Take a Snapshot once and reuse it:
var snapshot = db.GetSnapshot();
snapshot.TryGet(key, out var snapValue); // never reflects writes made after the snapshot was taken
using var snapIt = snapshot.CreateIterator();

db.Close(); // fsync snapshot, publish CURRENT, rotate WAL

// Ops tools, not normal runtime calls:
DB.Destroy(path);                 // delete exactly ZLDB's own files, not the directory wholesale
DB.Repair(path, new Options());   // explicit, opt-in salvage of an unopenable directory — see below

// Introspection (ADR §121), mirrors real LevelDB's DB::GetProperty — small named set, not a
// general query API:
db.GetProperty("zldb.num-entries", out var count);
db.GetProperty("zldb.approximate-memory-usage", out var bytes);
```

**No `byte[]`-returning `Get` anymore (ADR §118).** `TryGet` replaced it outright, not
alongside it — returning the array `MemTable` already owns instead of cloning it, safe because
`MemTable` never mutates an already-stored array in place (a later write to the same key swaps in
a new array, never edits the old one). `Put`/`Delete`/`WriteBatch.Put`/`WriteBatch.Delete` accept
`ReadOnlySpan<byte>` — a `byte[]` argument still compiles unchanged (implicit conversion), but a
caller with a `stackalloc`'d key no longer needs to heap-allocate one first. The key parameter of
`TryGet` itself stays `byte[]`, not a span — `MemTable`'s backing `ImmutableSortedDictionary` (see
ADR §119) has no span-based lookup overload, so accepting a span there would force an allocation to
do the lookup instead of removing one.

A storage-layer failure (disk full, permission denied) during a WAL append/flush or snapshot write
surfaces as `LevelDbIOException` (an `IOException` subtype — existing `catch (IOException)` call
sites keep working unchanged) with the original exception preserved as `InnerException`. Logical
format errors (bad/missing `CURRENT`, corrupted snapshot content, malformed batch encoding) keep
using `InvalidOperationException`/`InvalidDataException` — those already say clearly what's wrong
and aren't wrapped.

## Architecture (route B — simple KV)

```
RAM ImmutableSortedDictionary (lock-free, ADR §119)  =  source of truth (full dataset)
WAL (*.log)                                          =  durability for writes since last snapshot
Snapshot (*.ldb)                                     =  one live file named in CURRENT
```

Flush/Close: write live keys → **fsync** `.ldb` → publish `CURRENT` → rotate WAL → best-effort delete orphan `.ldb` / old `.log`.

`WriteOptions.Sync=true` fsyncs the **WAL** after append. Snapshot fsync happens on flush/Close (and when the write buffer forces a flush).

Crash mid-flush (new `.ldb` written, `CURRENT` still old): Open trusts `CURRENT` only; orphan `.ldb` is ignored and GC’d.

## On-disk format

Both the WAL and the snapshot use a table-driven CRC-32 (IEEE 802.3 polynomial, `Crc32.cs`) — plain
CRC-32, not CRC32C/Castagnoli like real LevelDB's WAL/SSTable checksums, since ZLDB's format is
already explicitly non-compatible with Mojang/RocksDB/NuGet LevelDB (no reader on the other end
needs the exact same polynomial). What *is* mirrored from real LevelDB is the checksumming
*strategy*: checksum type+payload, never the length field itself, and treat a checksum failure on a
structurally-complete record as different from a truncated tail (see below).

**WAL record** (`Journal.cs`), one per `Put`/`Delete`/`Write` call:

```
type:u8 | crc:u32 LE | bodyLen:u32 LE | body[bodyLen]
```

`crc = Crc32.Compute(type, body)` (over the type byte + body, not `bodyLen`). `body` is itself
`KvFraming`-length-prefixed content: `key + value` for Put, `key + empty value` for Delete, the
encoded `WriteBatch` payload for Batch. Two distinct replay outcomes:

- **Torn tail** (fewer than 9 header bytes remain, or `bodyLen` claims more bytes than the file
  has) → normal crash shape (writer died mid-append), replay stops silently, no error.
- **CRC mismatch on a structurally-complete record** → genuine corruption. Replay stops there and
  reports through `DB.OnCorruption` (an optional `Action<string>?` hook — this is a leaf library
  with no `Zenith.Diagnostics`/`ILogger` dependency, so it doesn't log anything unless a caller
  wires the hook) rather than trusting anything after it.

**Snapshot** (`TableFile.cs`, the `.ldb` named by `CURRENT`):

```
magic "ZLDB" | version u32=1 | count u32 | entries: (i32 klen | key | u32 vlen | value)* | crc:u32 LE
```

The whole snapshot is built in RAM first (consistent with ZLDB's "entire dataset fits in RAM"
constraint above), then written with a single trailing CRC-32 over everything before it. On load:
a magic mismatch means "not our format" (foreign/stale file, `TryLoadInto` returns `false`, caller
treats it as empty); a magic match with a CRC mismatch means "our format, but corrupted" and throws
`InvalidDataException` instead of silently discarding real data.

**`CURRENT`**: plain text filename + newline, written to `CURRENT.tmp` then `File.Move`'d over
`CURRENT` (atomic rename on the same volume). No directory-level fsync after the rename — real
LevelDB's own `CURRENT`-equivalent (`SetCurrentFile`/MANIFEST) doesn't do that either; it's an
accepted risk upstream, not something ZLDB uniquely lacks.

## `Destroy` and `Repair`

Two ops-facing static entry points, mirroring real LevelDB's `DestroyDB`/`RepairDB`:

- **`DB.Destroy(directory)`** deletes exactly the files ZLDB owns (`LOCK`, `CURRENT`/`CURRENT.tmp`,
  numbered `*.log`/`*.ldb`), removing the directory itself only if nothing else is left in it.
  Safer than a caller doing `Directory.Delete(dir, recursive: true)` blind.
- **`DB.Repair(directory, options)`** is an explicit, separate call — **never** invoked
  automatically by `Open()` or the public constructor, by design: a corrupted database always fails
  loudly by default (`DB.OnCorruption` for WAL corruption, a thrown `InvalidDataException` for a
  corrupted snapshot); repairing it is something an operator opts into, not a silent fallback that
  could mask real corruption as normal operation.

  Scope, honestly: unlike real LevelDB's `RepairDB` (which can recover individual valid
  blocks/SSTables out of a partially-damaged multi-file store), ZLDB's snapshot is one file with one
  whole-file trailing checksum — there's no finer-grained "this part is still good" to recover. So
  `Repair`'s snapshot handling is binary: a checksum failure discards the whole snapshot (not a
  partial salvage), and the DB is rebuilt from whatever the WAL still has (itself possibly a partial
  replay — everything before a corrupt point in the WAL survives, same as a normal open). `Repair`
  leaves the directory in a normal, openable state afterward.

## Verified by tests (`libs/leveldb.Tests`)

| Scenario | Coverage |
|----------|----------|
| Crash mid-flush (orphan `.ldb`, `CURRENT` old) | Manual orphan (CURRENT only → old value) + `AfterTableWriteBeforeCurrent` (CURRENT frozen; WAL still recovers Puts; orphan GC’d) |
| `CURRENT` missing + `CreateIfMissing=false` | Fails with clear message |
| `CURRENT` garbage / missing table | Treated as stale; empty start when create allowed |
| Truncated WAL tail | Complete records kept; torn header/body treated as EOF, not an error |
| WAL mid-stream corruption (CRC mismatch on a complete record) | Replay stops there; reported via `Options.OnCorruption` |
| Corrupted snapshot (`.ldb` CRC mismatch) | `TryLoadInto` throws `InvalidDataException` instead of loading bad data |
| `Snapshot` isolation | `GetSnapshot()`'s view never reflects writes made after it was taken |
| `LevelDbIOException` | Is-an-`IOException`, preserves the original cause |
| `Destroy` | Removes only ZLDB-owned files (a foreign file in the same dir survives); no-op on a missing directory |
| `Repair` | Normal open still fails loudly on a corrupted snapshot; `Repair` then reopens cleanly (losing only that snapshot's data) and a healthy DB is unaffected by `Repair` |
| Put after Close | `ObjectDisposedException` (API not concurrent Put+Close) |
| Second `DB` on the same directory | Rejected with a clear message; `Close()` releases the lock so a later open succeeds; a failed `Open()` (e.g. corrupted snapshot) releases it too, instead of leaking it |
| Concurrent reads+writes (lock-free `MemTable`, ADR §119) | Many threads `Put`ing distinct keys while others `CreateIterator`/`TryGet` concurrently: no exceptions, no deadlock, final count exactly matches writes made |
| Same-key concurrent overwrite race | Every concurrent `TryGet` on a hot key observes one complete, self-consistent value or none — never a torn mix of an old and a new write's bytes |
| `Seek` binary search (ADR §120) | Exact match at first/middle/last key, landing between two keys, before the first key, past the last key, empty snapshot, full ordered iteration on a 500-entry shuffled-insert dataset |
| `Destroy`/`Repair` refuse when locked | Both throw a clear message if another instance has the directory open; `Destroy` succeeds once that instance closes |
| Empty key / empty value | Round-trips across reopen |
| Large value (2MB) | Round-trips through both the WAL and a flushed `.ldb` |
| Same-key put/delete churn | WAL replay after "crash" (no `Close()`) recovers exactly the last write |
| Tombstone lifecycle | `DropTombstones` removes dead keys from RAM (don't resurrect); `GetSnapshot` excludes deleted keys even before any flush |
| Multiple iterators over one `Snapshot`, used concurrently | Safe — entries are never mutated after the snapshot is built |
| Concurrent `Close()` from multiple threads | Idempotent, no exceptions, lock released exactly once |
| `WriteBatch` same-key multi-op | Last write in the batch wins, matching the class's own documented example |
| `MemTable`, `ByteComparer`, `Crc32`, `KvFraming` (internal, direct) | Unit-tested in isolation via `InternalsVisibleTo` — see `LevelDbEdgeCaseTests.cs` |
| Missing `CURRENT` target, ZLDB-shaped name | Throws `InvalidDataException`; non-ZLDB-shaped name still resets empty; `Repair` tolerates either |
| `GetProperty` | Reports entry count and approximate memory usage; `false` for an unrecognized name |
| `WriteBatch.ApproximateSize` | Grows with buffered ops, resets on `Clear` |

## Why not a full LSM + compaction?

Zenith only needs a trustworthy KV for two key families, one world, one process — no Mojang decode. A theatrical LSM without multi-level compaction was complexity without proven need (`ARCHITECTURE.md` rule 7). Steady-state already was a single rewritten table; adding L0/MANIFEST/compaction would buy risk, not product.

## Optional later (not P1)

- Periodic background snapshot without write-path stall under lock
- Directory fsync after the `CURRENT` rename (real LevelDB doesn't do this either — see "On-disk format" above; would only matter if ZLDB wants to be stricter than the reference it's modeled on)

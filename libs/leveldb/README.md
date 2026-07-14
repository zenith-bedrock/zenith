# Zenith.LevelDB

Managed key/value store for Zenith chunk overlay storage (`c:` / `ov:` keys). Own on-disk format (ZLDB) — **not** binary-compatible with `LevelDB.Standard`, Mojang Bedrock worlds, or RocksDB.

## Constraint (read this first)

**The entire dataset must fit in RAM while the database is open.**  
On open, the snapshot is loaded into a `SortedDictionary` that is the runtime source of truth; Get/Iterator never stream cold SSTs from disk. That is a conscious trade for Zenith’s sparse `c:`/`ov:` overlays in a single process — not “we forgot Bloom filters.” If you need datasets larger than host memory, this store is the wrong tool (revisit streaming/LSM then).

Recreate `world.path` after format changes; no migration from NuGet LevelDB dirs.

## API

```csharp
using var db = new DB(new Options { CreateIfMissing = true }, path);

db.Put(key, value);
db.Delete(key);

var batch = new WriteBatch();
batch.Put(keyA, valueA);
batch.Delete(keyB);
db.Write(batch, new WriteOptions { Sync = true }); // fsync WAL

using var it = db.CreateIterator();
it.Seek(prefix);
while (it.IsValid()) { /* ... */ it.Next(); }

db.Close(); // fsync snapshot, publish CURRENT, rotate WAL
```

## Architecture (route B — simple KV)

```
RAM SortedDictionary  =  source of truth (full dataset)
WAL (*.log)           =  durability for writes since last snapshot
Snapshot (*.ldb)      =  one live file named in CURRENT
```

Flush/Close: write live keys → **fsync** `.ldb` → publish `CURRENT` → rotate WAL → best-effort delete orphan `.ldb` / old `.log`.

`WriteOptions.Sync=true` fsyncs the **WAL** after append. Snapshot fsync happens on flush/Close (and when the write buffer forces a flush).

Crash mid-flush (new `.ldb` written, `CURRENT` still old): Open trusts `CURRENT` only; orphan `.ldb` is ignored and GC’d.

## Verified by tests (`libs/leveldb.Tests`)

| Scenario | Coverage |
|----------|----------|
| Crash mid-flush (orphan `.ldb`, `CURRENT` old) | Manual orphan (CURRENT only → old value) + `AfterTableWriteBeforeCurrent` (CURRENT frozen; WAL still recovers Puts; orphan GC’d) |
| `CURRENT` missing + `CreateIfMissing=false` | Fails with clear message |
| `CURRENT` garbage / missing table | Treated as stale; empty start when create allowed |
| Truncated WAL suffix | Complete records kept; torn batch skipped (**no CRC yet**) |
| Put after Close | `ObjectDisposedException` (API not concurrent Put+Close) |

## Why not a full LSM + compaction?

Zenith only needs a trustworthy KV for two key families, one world, one process — no Mojang decode. A theatrical LSM without multi-level compaction was complexity without proven need (`ARCHITECTURE.md` rule 7). Steady-state already was a single rewritten table; adding L0/MANIFEST/compaction would buy risk, not product.

## Optional later (not P1)

- WAL framing + CRC (torn last record)
- Periodic background snapshot without write-path stall under lock

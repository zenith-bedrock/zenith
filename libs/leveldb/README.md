# Zenith.LevelDB (ZLDB)

Managed, ordered key-value store in the LevelDB shape, built for Zenith's world storage (ADR §11, §61, §168).
It has its **own on-disk format**: not compatible with Google LevelDB, Mojang Bedrock worlds or RocksDB. Reading and
writing Mojang worlds is a separate backend behind `IChunkStorage`, not this library.

## How it works

```
write  ->  WAL (*.log, CRC per record)  ->  memtable (RAM, sorted, immutable map)
                                              | when it reaches Options.WriteBufferSize
                                              v
                                        new table (*.ldb): sorted data blocks + bloom filter + index + footer
                                              | background size-tiered compaction merges neighbours
                                              v
read   ->  memtable, then tables newest -> oldest (a tombstone stops the search)
```

- **Larger than RAM.** Only the memtable and, per table, one index key per block plus a bloom filter stay resident.
  A lookup reads at most one data block, and none when the bloom filter rules the table out.
- **Flush cost is the buffer, not the database.** A flush writes only the memtable. Compaction rewrites a handful of
  similarly sized tables at a time; large old tables are left alone until neighbours reach their size.
- **Lock-free reads (ADR §119).** The memtable and the table set are immutable values behind one reference each, and
  tables are read-only files read with positional I/O. Writers are serialized by one gate. A reader reads the
  memtable first and the table set second; a flush publishes the new set before emptying the memtable.
- **Snapshots are cheap.** A `Snapshot` is the memtable's entries (one array copy, bounded by the write buffer) plus
  the table set it saw. Later writes cannot reach it; an `Iterator` merges both and skips deleted keys.
- **Durability.** `Write(batch)` appends to the WAL before touching the memtable (`WriteOptions.Sync` fsyncs).
  Tables are written under a `.tmp` name, fsynced, renamed, then listed in `CURRENT` (written and renamed the same
  way). A crash between those steps leaves an unlisted table, deleted on the next open; the log replays on top.
- **Integrity.** CRC-32 on every WAL record, data block, bloom, index and footer. A block that fails its checksum
  throws `InvalidDataException` on read instead of returning wrong bytes. A table that fails at open fails the open;
  only `DB.Repair` drops it.

## Files

| File | Content |
|------|---------|
| `CURRENT` | table names, oldest first, one per line |
| `NNNNNN.ldb` | one table (layout in `Table.cs`) |
| `NNNNNN.log` | write-ahead log of what the memtable holds |
| `LOCK` | OS-level exclusive lock for the directory |

## API

```csharp
using var db = new DB(new Options { CreateIfMissing = true }, path);
db.Put(key, value);
db.Delete(key);
db.Write(batch);                      // atomic across keys
if (db.TryGet(key, out var value)) { }
using var it = db.CreateIterator();   // or db.GetSnapshot().CreateIterator()
it.Seek(prefix);
db.GetProperty("zldb.num-tables", out var tables);
DB.Repair(path, new Options());       // explicit, never automatic
DB.Destroy(path);                     // deletes only ZLDB's own files
```

`Options`: `WriteBufferSize` (4 MiB), `BlockSize` (16 KiB), `BloomBitsPerKey` (10), `CompactionFanIn` (4),
`MaxTableCount` (12), `BackgroundCompaction` (true; `DB.CompactNow()` runs it on the calling thread).

Properties: `zldb.num-entries` (an upper bound: tombstones and overwritten versions count until compaction folds
them), `zldb.num-tables`, `zldb.approximate-memory-usage`.

## Errors

Storage failures (disk full, permission denied) surface as `LevelDbIOException`. Format problems surface as
`InvalidDataException`: a `CURRENT` that names a ZLDB-shaped table missing from disk, a corrupt table, a malformed
batch. A listed file that is not a ZLDB table at all (a leftover `MANIFEST-…`) is foreign residue and is skipped.

## Limits

- Values are read into a fresh array per lookup; there is no block cache (the OS file cache does that job).
- One writer at a time, one process per directory.
- Compaction is size-tiered: a read can touch several tables, and the bloom filters are what keep that cheap.

# ADR §168 — Store records by typed key behind a write-behind layer, on a paged ZLDB

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §168`.

**Context:** After §167 a column is one blob of kilobytes, so the world store grows with the world. Three things stood in the way. ZLDB kept the whole dataset in RAM and rewrote it as one snapshot file on every flush, so memory and flush cost grew with the world. `LevelDbChunkStorage` was 240 lines: a queue, a worker, a drain forced before every read, and five copies of the same get/put pair. Keys were UTF-8 strings (`"c:12:-3"`), which allocate per call and sort as text, so neighbouring chunks were not neighbours on disk.

**Choice:**
- `RecordKey` (kind byte + payload) is the one key type. Column coordinates are big-endian with the sign bit flipped, so key order is world order. `IChunkStorage` is three calls (`GetAsync`, `PutAsync`, `FlushAsync`) plus a diagnostics property; what each `RecordKind` means lives in `ChunkStorageExtensions`.
- `WriteBehindStorage` owns the queue: puts land in a concurrent map (the newest value of a key wins), and one worker writes the map as a single atomic batch at least every 50 ms. A read returns a pending record from memory, so nothing forces a drain. `IRecordStore` is the synchronous durable backend under it; `LevelDbRecordStore` is the ZLDB one.
- ZLDB v2 is a small LSM: WAL, memtable, immutable sorted tables (data blocks, bloom filter, index, footer, CRC on each), `CURRENT` listing the tables, size-tiered background compaction. A flush writes only the memtable. Resident memory is the memtable plus one index key per block and a bloom filter per table.
- Snapshots and iterators merge the memtable copy and the table set; both are immutable, so readers still take no lock (§119).
- NBT: array and list lengths are checked against the bytes left before anything is allocated, varints that overflow are refused, and the library gained concatenated-root reads, the `level.dat` header and a fluent `NbtCompound`.

**Why:** A paged engine of our own keeps the DX and the single-process model, avoids a native dependency, and leaves the world format free to become Zenith-specific later. A Mojang-compatible LSM would have tied key and block formats to Bedrock's, which the §167 column blob does not follow. Dragonfly and Minestom both separate "what a record is" from "how a store holds it"; this is the same cut.

**Invariants:** Key order equals numeric order for columns (`RecordKeyTests`). A read sees its own pending writes (`WriteBehindStorageTests`). A flush's size is bounded by the write buffer, and resident memory stays far below the data size (`PagedEngineTests`). A damaged block throws instead of returning bytes. An unlisted table left by a crash is deleted on open. Old worlds are not read: the key and file formats changed, and nobody runs Zenith yet.

**Non-goals:** Reading or writing Mojang worlds (a separate backend and converter, §61). A block cache. Multi-process access. Leveled compaction.

**Deferred:** A converter to and from Bedrock subchunks. A block cache if disk reads show up in a profile. Directory fsync after the `CURRENT` rename.

**Verification:** `leveldb.Tests` (88): paged reads, tombstones across tables, merged iteration, compaction, background compaction under writes, corruption in a block and in the footer, bloom and varint. `nbt.Tests` (24). `zenith.Tests`: key encoding and ordering, write-behind read-your-writes and coalescing. Not measured: compaction cost on a multi-gigabyte world, read latency on a cold disk.

**Status (November 2026):** Shipped. Carried by `libs/leveldb/{DB,Table,Cursors,Snapshot,Iterator,BloomFilter,Varint}.cs`, `libs/nbt`, `World/{RecordKey,IChunkStorage,IRecordStore,LevelDbRecordStore,WriteBehindStorage,LevelDbChunkStorage}.cs`.

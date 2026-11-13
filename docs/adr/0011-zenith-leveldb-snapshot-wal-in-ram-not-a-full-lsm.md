# ADR §11 — Zenith.LevelDB = snapshot + WAL in RAM, not a full LSM

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §11`.

**Choice:** Collapse the early “mini-LSM” (mem + journal + merge iterator) into: **full dataset in a `SortedDictionary`**, WAL for durability, one fsynced snapshot named by `CURRENT`. Public API (`DB` / `WriteBatch` / `Iterator`) unchanged.

**Why:** Callers only need trustworthy `c:` / `ov:` KV in one process — not Mojang decode or RocksDB-scale compaction. Steady-state already rewrote a single table; adding L0/compaction would be storage-engine risk without a proven product need (`ARCHITECTURE.md` rule 7).

**Explicit constraint:** the **entire dataset must fit in RAM** while open. Documented in [`libs/leveldb/README.md`](../../libs/leveldb/README.md). Crash mid-flush (new `.ldb`, old `CURRENT`) recovers by trusting `CURRENT` only; orphans are GC’d.

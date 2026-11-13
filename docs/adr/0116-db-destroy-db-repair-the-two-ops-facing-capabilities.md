# ADR §116 — `DB.Destroy`/`DB.Repair` — the two ops-facing capabilities §115 left out

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §116`.

**Choice:** Continue the §115 comparison against `mojang-leveldb`'s full public `DB`/`Options`/
`WriteBatch` API surface (not just WAL/snapshot internals this time) to find what else is missing.
Most of it is correctly non-applicable (block cache, Bloom filters, compression, pluggable
`Comparator`/`Env`, `CompactRange`, `max_open_files`/`block_size`/etc. — all multi-file-LSM-era
knobs ZLDB's RAM+WAL+snapshot shape has no use for, reaffirming §11/§61, not revisited). Two
capabilities were genuine, proportionate gaps and are now shipped:

1. **`DB.Destroy(directory)`** — deletes exactly ZLDB's own files (`LOCK`, `CURRENT`/`CURRENT.tmp`,
   numbered `*.log`/`*.ldb`), not the directory wholesale; removes the directory itself only if
   nothing else remains. Mirrors real LevelDB's `DestroyDB`, safer than a caller's own
   `Directory.Delete(dir, recursive: true)`.
2. **`DB.Repair(directory, options)`** — the direct, valuable follow-on to §115's checksum work: a
   corrupted snapshot previously meant `Open()` threw and the directory stayed unopenable with no
   recovery path. `Repair` is an **explicit, separate static call — never automatic**. Product
   decision made deliberately (not the only option considered): `Open()`/the public constructor
   must keep failing loudly on corruption by default; a corrupted DB silently limping back to life
   on the next boot would risk masking real corruption as normal operation. An operator opts into
   `Repair` on purpose. Internally implemented as an existing-machinery reuse, not new recovery
   logic: a private constructor overload opens tolerantly (treats a checksum-failed snapshot as
   "absent," same fallback path already used for a foreign/non-ZLDB `CURRENT` target), then the
   normal open sequence's own flush-and-rewrite step produces a clean `CURRENT`/snapshot/journal as
   a side effect. Scope is honestly narrower than real LevelDB's repair (which recovers individual
   valid blocks/SSTables from a partially-damaged multi-file store) — ZLDB's single whole-file
   snapshot checksum has no finer granularity, so a failed snapshot is discarded entirely, not
   partially salvaged; only the WAL's own partial-replay tolerance (already existed pre-§115)
   carries data through `Repair`.

**Why now:** direct continuation of §115, at the user's request to keep comparing against
`mojang-leveldb` for what ZLDB still lacks. Same non-goal boundary as §115 — nothing here is
compaction/LSM-adjacent.

**Status (ago 2026):** Shipped — `DB.Destroy`, `DB.Repair`, private tolerant-open constructor
overload. `libs/leveldb.Tests` grew from 15 to 19 cases (Destroy file-scoping + no-op-on-missing,
Repair discarding a corrupted snapshot + reopening cleanly, Repair no-op-on-healthy-DB); all 19
green, full `zenith.sln` build/test unaffected.

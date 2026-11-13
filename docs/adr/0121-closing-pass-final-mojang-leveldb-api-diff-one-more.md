# ADR §121 — Closing pass: final `mojang-leveldb` API diff, one more inconsistency fixed

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §121`.

**Choice:** At the user's explicit request for a final "have we covered all the terrain" check
before closing this line of work, re-diffed ZLDB's shipped surface (§115-120) against real
LevelDB's full public API (`db.h`/`options.h`/`write_batch.h`, already surveyed in §115/§118's
research) item by item rather than assuming coverage. Confirmed everything applicable is shipped
(checksums, real `LockFile`, `Destroy`/`Repair`, lock-free concurrent reads — which now beats real
LevelDB's own approach for ZLDB's workload, O(log n) `Seek`) and everything unshipped is a
correctly-scoped non-goal (compaction, block cache, Bloom filter, compression, pluggable
comparator/env — all multi-file-LSM machinery ZLDB's RAM+WAL+snapshot shape has no use for). Found
one more real inconsistency and closed two small completeness gaps, all confirmed with the user
before implementing (a product-behavior change deserved sign-off, not a unilateral call):

1. **A missing `CURRENT` target was always treated as harmless, even when it looked like our own
   file.** `Open()` had one fallback for "the file `CURRENT` names doesn't exist" — silently reset
   empty, on the theory it's leftover foreign-format residue (e.g. a native LevelDB `MANIFEST`).
   But that conflated two different signals: a name that isn't shaped like anything ZLDB would
   produce (safe — genuinely never ours) and a name that *is* shaped like our own `{n:D6}.ldb`
   convention but is simply gone (alarming — that file should exist and doesn't, which looks like
   real data loss). This was inconsistent with §115's own "fail loud on corruption" stance for a
   CRC-mismatched snapshot. Fixed: a `LooksLikeZldbTableName` check (`.ldb` extension, purely
   numeric stem — the same lenient shape `EnumerateNumberedFiles` already parses) now makes the
   first case throw `InvalidDataException`, same treatment as a checksum failure; the second case
   is unchanged. `Repair`'s `tolerateCorruptSnapshot` flag covers both uniformly, since `Repair`'s
   own documented policy ("discard the snapshot entirely, rebuild from WAL") already applies
   either way.
2. **`DB.GetProperty(string, out string)`** — mirrors real LevelDB's `DB::GetProperty`. ZLDB has no
   use for most of its property set (`"leveldb.num-files-at-level<N>"`, `"leveldb.sstables"` — all
   multi-file/leveled-storage concepts), but `MemTable.Count`/`ApproxSize` already existed
   (§119, unit-tested in isolation, never exposed through `DB`) — `"zldb.num-entries"` and
   `"zldb.approximate-memory-usage"` give them an actual caller-facing purpose. Unrecognized names
   return `false`, never throw.
3. **`WriteBatch.ApproximateSize`** — mirrors `WriteBatch::ApproximateSize`. Trivial (`_ops.Length`
   as a public property), zero risk, closes a small parity gap noted but deferred back in §118.

**Why now:** explicit closing check requested by the user before ending this line of work — "só
vamos fechar caso tenhamos coberto todo terreno do leveldb."

**Non-goals reaffirmed:** everything already reaffirmed in §115-120 stays reaffirmed; nothing new
opened here. `GetApproximateSizes`/`CompactRange` remain correctly out of scope (leveled-storage
concepts).

**Status (ago 2026):** Shipped — `DB.cs` (`CURRENT`-target check, `GetProperty`), `WriteBatch.cs`
(`ApproximateSize`). Six new `libs/leveldb.Tests` cases (72 total, up from 66), green across
repeated runs; full `zenith.sln` build and 1019-case `zenith.Tests` run green.

# ADR §118 — ZLDB read path: reader/writer lock, zero-alloc `TryGet`, span-based writes

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §118`.

**Choice:** Fourth pass of the `mojang-leveldb` comparison, this time at the user's explicit
request to draw performance inspiration from engines like RocksDB (still bounded by §11/§61's
non-negotiable shape: RAM `SortedDictionary` + WAL + one snapshot, no compaction/LSM). Investigated
the literal terms the user cited — "Mutex, MutexGuard, ConcurrentBufferPool into Database's Read
Path" — against every cloned reference repo (`mojang-leveldb`, `dragonfly`, `gophertunnel`,
`PowerNukkitX`, `pocketmine`, etc.): zero matches. Not a quote, a design direction to execute.

1. **Reader/writer lock replaces the single mutex.** `_gate` (a plain `object`) → `_lock`
   (`ReaderWriterLockSlim`, `NoRecursion`), with two small `readonly struct IDisposable` guards
   (`ReadGuard`/`WriteGuard`) giving the same `using (...)` RAII ergonomics the old `lock` block
   had. `TryGet`/`CreateIterator`/`GetSnapshot` take the read lock (concurrent with each other now,
   previously fully serialized); `Write`/`Close`/the flush they trigger take the write lock
   (unchanged: still fully exclusive — ZLDB's single-writer model doesn't change, only reads stop
   blocking other reads).
2. **`TryGet` replaces `Get` outright — removed, not kept alongside.** Investigated `MemTable`
   closely first: `Put`/`Delete` always clone key/value *on write* and never mutate an
   already-stored array in place — a later write to the same key swaps in a brand-new array, never
   edits the old one. That means any array already handed to a caller stays valid and immutable
   forever, which made `DB.Get`'s defensive `(byte[])value.Clone()` before every return provably
   redundant. `TryGet(byte[] key, out ReadOnlyMemory<byte> value)` returns the `MemTable`-owned
   array directly. The key parameter deliberately stays `byte[]`, not `ReadOnlySpan<byte>`:
   `SortedDictionary` (what `MemTable` is built on, to preserve iteration order without
   reimplementing a tree) has no span-based lookup overload the way `Dictionary` gained recently —
   accepting a span for the key would force a temporary allocation to do the lookup, making this
   method *more* allocating on the key side than today, not less. Spanifying only where it's
   honestly a win, not for API uniformity's own sake.
3. **`Put`/`Delete`/`WriteBatch.Put`/`WriteBatch.Delete` accept `ReadOnlySpan<byte>`.** A durable KV
   store must materialize an owned copy internally either way — this doesn't remove that copy — but
   it removes the requirement that the *caller* already own a `byte[]` before calling; a caller can
   now build a key in a `stackalloc` buffer without allocating first. `byte[]` still compiles at
   every existing call site unchanged (implicit conversion to `ReadOnlySpan<byte>`).
4. **`GetSnapshot`/`CreateIterator` stop cloning key+value per entry.** `snap[(byte[])k.Clone()] =
   (byte[])v.Clone()` → `snap[k] = v` — safe under the same copy-on-write discipline as (2). This
   was the single most expensive point in the read path: an O(n) clone of the *entire* live
   dataset, on every `ForEachOverlayAsync`/`ForEachChestAsync` boot-time scan. `Iterator.Key()`/
   `Value()` and the new `Snapshot.TryGet` follow the same `ReadOnlyMemory<byte>` shape.
5. **`LevelDbChunkStorage.cs`** (the sole consumer, confirmed by grep — nothing else in the repo
   references `Zenith.LevelDB`) updated to the new signatures. `Put`/`Delete` call sites needed no
   change (implicit span conversion); `Get` call sites became `TryGet(...).ToArray()`, and
   `ForEachOverlayAsync`/`ForEachChestAsync`'s manual `StartsWith` helper was deleted in favor of
   the built-in `ReadOnlySpan<byte>.StartsWith` extension.

**Honest limit of the "zero-alloc" claim:** `IChunkStorage`'s public contract (used throughout
`src/zenith/World/*.cs` and beyond) still returns `byte[]?` and is **not** touched by this ADR. For
a single-key read reached through `LevelDbChunkStorage`, the clone this ADR removes from inside
`DB.TryGet` reappears as one `.ToArray()` at `LevelDbChunkStorage`'s own boundary — the allocation
*moves*, it doesn't disappear end-to-end, for that specific path. The gain that *is* complete with
no asterisk: the `GetSnapshot`/`CreateIterator` O(n) clone elimination (item 4), and the removal of
allocation *pressure* on write-path callers (item 3). Modernizing `IChunkStorage` itself to close
the remaining gap is real, deliberately-out-of-scope follow-up work, not started here.

**Non-goals reaffirmed:** `ArrayPool<byte>`/buffer pooling — investigated and rejected for this
pass; the actual win was eliminating unnecessary clones the existing copy-on-write discipline
already made redundant, not pooling (which would buy rent/return complexity and use-after-return
risk without a concrete, measured hot path that needs it). Swapping `SortedDictionary` for a
span-lookup-capable structure — no proven need; the key side was never the expensive part.
Compaction/multi-level LSM/block cache/Bloom filters — still §11/§61, untouched.

**Status (ago 2026):** Shipped — `DB.cs` (lock model, `TryGet`, span `Put`/`Delete`,
`GetSnapshot`), `WriteBatch.cs`, `Iterator.cs`, `Snapshot.cs`, `LevelDbChunkStorage.cs`. All 22
`libs/leveldb.Tests` cases updated to the new API and green (behavior unchanged, only call-site
shape); full `zenith.sln` build and 1019-case `zenith.Tests` run unaffected.

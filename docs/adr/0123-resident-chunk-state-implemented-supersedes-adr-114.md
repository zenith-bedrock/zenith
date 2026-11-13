# ADR §123 — Resident chunk state implemented — supersedes ADR §114 (design-only)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §123`.

**Choice:** `docs/adr/0114-resident-chunk-state.md` documented a hydrate/evict lifecycle for
overlays/chests but shipped no code. Implemented now — re-verifying the design against current code
first surfaced four corrections to the original plan (all confirmed with the user before building):

1. **RAM ceiling correction.** `Zenith.LevelDB` is a deliberately always-fully-resident, non-LSM
   store (ADR §11/§61 non-goal: "RAM-resident dataset, must fit in RAM"). The entire persisted
   overlay/chest dataset already lives in `LevelDbChunkStorage`'s `DB` regardless of what `World`
   does. This feature does **not** bound total process RAM by proximity — it removes the **duplicate**
   copy `World`/`ChestStore` keep on top of what ZLDB already holds. Real, but partial: less
   `Dictionary`/GC overhead for chunks nobody's near, not a shrink of the underlying dataset. Anyone
   reaching for this feature expecting the first thing should read this section before promising it.
2. **`FloorDropStore` dropped from scope.** No `PutFloorDropAsync`/`GetFloorDropAsync` exists
   anywhere — floor drops are pure RAM, already bounded by `FloorDropStore.SoftCap` and per-tick
   despawn. ADR §114's "three side-tables" framing was stale; only overlays + chests apply.
3. **New chunk-prefixed binary key format for `ov:`/`ct:`, replacing decimal-ASCII per-block keys.**
   The old `ov:x:y:z` text keys have no chunk grouping and don't sort negative coordinates
   consistently, so a per-chunk scan was structurally impossible, not just "nontrivial" as §114
   guessed. New layout (`WorldStorageKeys.cs`) mirrors real BDS's own chunk-key shape, confirmed via
   `D:\Development\bedrock\PowerNukkitX`/`Basalt`: `chunkX:i32LE(4) + chunkZ:i32LE(4) + tag:u8(1) +
   localX:u8(1) + y:i32LE(4) + localZ:u8(1)`, 15 bytes. `Seek(chunkX+chunkZ prefix) +
   StartsWith(prefix)` finds one chunk's entries without a numeric-sort assumption — same reasoning
   BDS's fixed-width prefix relies on. Breaking change, no migration — early-stage project, explicit
   user call to prioritize a well-architected format over save compatibility.
4. **Residency tracking: incremental per-chunk viewer refcount** (`ChunkResidencyIndex`), not a
   periodic-sweep aggregation — chosen over §114's own two proposed options. Wired into every point
   `ChunkStreamSystem` adds/removes a chunk from a player's `PlayerChunkTracker._known` (`TryBegin`,
   `ForgetOutsideRadius`, `TryAbandon`, and `RememberMany` — the pre-spawn path §114's original options
   didn't account for), plus `PlayerQuitEvent` cleanup (a disconnecting player never calls
   `ForgetOutsideRadius`/`TryAbandon` for their remaining known chunks, which would otherwise leak
   those chunks as permanently "viewed"). A first pass had `RememberMany`'s own return value drive
   `Acquire`, which meant a pre-spawning player's chunks were *hydrated* (via `StreamRadiusAsync`)
   before residency ever saw a viewer for them — a same-tick sweep could (rarely, harmlessly, since
   nothing on disk is touched) evict a chunk mid-join. Fixed before shipping: `StartPendingPreSpawn`
   now acquires the whole pre-spawn square synchronously, in the same tick the request is consumed,
   before `StreamRadiusAsync`/hydration starts; `RememberMany`'s call site no longer double-acquires.

Shape: hydrate-on-first-touch replaces `World`'s old boot-time full-table load (hooked into
`GetOrCreateColumnCoreAsync`, race-free for free via `_generationBroker`'s existing per-coordinate
single-flight coalescing). `ChunkResidencySystem` sweeps every
`ServerConfig.World.ChunkResidencySweepIntervalTicks` ticks (default 1200 = 60s), evicting chunks
with zero viewers: chests are explicitly persisted before removal (chest-UI mutations aren't
persisted until close, unlike overlays which are already fire-and-forget-persisted on every write —
ADR §41) and a chest with an open UI is left resident regardless (belt-and-suspenders). No
grace-period/thrash-avoidance state — a boundary-oscillation evict-then-rehydrate is cheap since
ZLDB never actually drops the data. `DB.GetProperty` (ADR §121) backs a new diagnostic log line
(`World.LogResidencySnapshot`) comparing resident-in-`World` counts against ZLDB's total —
correlative operator signal, not proof the feature "worked" (see point 1).

**Status (ago 2026):** Shipped — `WorldStorageKeys.cs`, `IChunkStorage.cs`,
`LevelDbChunkStorage.cs`, `World.cs`, `ChestStore.cs`, `ChunkResidencyIndex.cs` (new),
`ChunkResidencySystem.cs` (new), `PlayerChunkTracker.cs` (`ForgetOutsideRadius`/`RememberMany` now
return what changed), `ZenithServer.cs`, `ServerConfig.cs`. New tests:
`LevelDbChunkStoragePerChunkTests`, `ChunkResidencyIndexTests`, `ChunkResidencyEvictionTests`,
`ChunkResidencySystemTests`,
`IntentContractTests.ChunkStreamSystem_acquires_residency_for_the_whole_pre_spawn_square_before_streaming_starts`
(point 4's fix), plus `WorldStorageKeys`/`PlayerChunkTracker` coverage for the new key format and
return values. `docs/adr/0114-resident-chunk-state.md` is now historical context only — this entry
is the current record.

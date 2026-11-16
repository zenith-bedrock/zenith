# ADR §167 — Keep the world in mutable columns, with block entities inside them

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §167`.

**Context:** Edits lived in a per-block overlay on top of regenerated terrain, capped at 10,000 entries. Past the cap, placing blocks was refused. Chests, and later furnaces, each needed their own store, soft cap and hydration path. There were no sections, so nothing could tick or sleep per 16³ volume. Coherence between what a client saw and what the server held only held for resident chunks.

**Choice:**
- A resident `WorldColumn` (24 `Section`s, y −64..319) is the single source of truth. Terrain is consulted only when a column has no stored copy. A stored copy always wins.
- `Section` is a palette plus bit-packed cells (uniform sections cost one id). Reads are lock-free; writes come from the owner thread and use a seqlock so stream workers can take consistent snapshots (`TrySnapshot`). The writer bumps the column `EditVersion` after the section change; a reader reads the version before building.
- The LevelChunk payload is serialised from the live sections (`SubChunkWire`) and cached by `EditVersion`. Each session records the version it was sent, and `ColumnSend.ResyncColumns` re-sends only columns that moved.
- Block entities (`BlockEntity`, `BlockEntityType` with id/create/encode/decode) live inside the column. `ContainerEntity` is the chest; `ChestStore` is a view over it. A furnace is another type, not another store.
- Storage is one blob per column (`ColumnCodec`, format "ZC" v1). Block ids are written as canonical state strings (`BlockPalette.CanonicalKeyOf`), so runtime ids can be renumbered without breaking saves. Corrupt blobs throw `InvalidDataException`; unknown entity types are dropped.
- Persistence is write-behind and dirty-only: at most 64 columns per residency sweep, on eviction, and on flush. The dirty flag is cleared before encoding, so a racing edit re-dirties the column.
- `ColumnAt` loads an unresident column synchronously and counts it (`SyncLoadCount`). `TryGetBlock` and `IsLoaded` never load, so ticking systems cannot wake unloaded chunks.

**Why:** Overlays cannot give per-section ticking, and each new stateful block would repeat the chest store work. Minestom, Dragonfly and Hytale all keep mutable sections with entities attached to the chunk; the overlay shape matches none of them. Old worlds are not preserved: nobody runs Zenith yet, and the user chose a deep refactor over a migration.

**Invariants:** A client never receives a column older than the world (`ColumnSendTests`). An evicted edited column reloads identically (`ChunkResidencyEvictionTests`). An untouched column is never written. There is no cap on edited cells (`WorldColumnModelTests`). An open container keeps its column resident.

**Non-goals:** Compatibility with the old overlay format. Cross-column atomic edits.

**Deferred:** Per-section ticking with Sleep/Continue, random ticks, fluids and the JSON component layer, in that order (`plans/world-dynamics-2026-11-15.md`). Chunk-load wake policy for ticking systems.

**Verification:** Unit suite (column, eviction, resync, persistence, chest and lid tests). Not yet measured: memory per resident column, sync-load frequency under a real client, and the write-behind cost with many dirty columns. Wire smoke is run separately.

**Status (November 2026):** Shipped in stage 1 of the world refactor. Carried by `World/Section.cs`, `WorldColumn.cs`, `ColumnCodec.cs`, `SubChunkWire.cs`, `BlockEntity.cs`, `ChestStore.cs`, `World.cs`, `Protocol/ColumnSend.cs`.

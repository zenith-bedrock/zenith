# ADR §62 — World domain map — Zenith-native seams (no BDS)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §62`.

**Choice:** Document World as a **façade** over named subdomains; keep files flat under `src/zenith/World/` until a later ADR forces folder cuts. Add two seams only:

1. **`ITerrainProvider`** — base column payload (+ matching `SampleBaseBlock` for SoftCap / GetBlock). Default = `FlatTerrainProvider` (`ChunkPayloads.BuildFlatOverworld`). Future noise/gen plugs here — **not** via Mojang storage (§61).
2. **`WorldStorageKeys`** — SSOT for Zenith KV prefixes (`c:` / `ov:` / `ct:` / `inv:` / `pd:`). Reserve Mojang namespaces (`player_`, `player_server_`, `~local_player`) — never write them from Zenith backends.

| Subdomain | Owns | Today |
|-----------|------|--------|
| Terrain base | Column when storage miss / heal | `ITerrainProvider`, `ChunkPayloads` |
| Overlay grid | Sparse permanent edits + SoftCap | `World` `_blockOverrides` / `_overlaysByChunk` |
| Persistence port | Put/Get/Flush | `IChunkStorage`, `LevelDbChunkStorage`, InMemory |
| Block registry | Palette + curated + profiles | `Blocks`, palettes, `DigProfiles`, `Tools`, `BreakDuration` |
| Containers | Chest RAM + pairing | `ChestStore`, `ChestPairing`, `ChestFacing` |
| Floor / gravity | Cell stores | `FloorDropStore`, `GravityPendingStore` |
| Packed blobs | On-disk formats | `SlotBlob`, `PlayerDataBlob` |

**Explicit:** `inv:` / `pd:` hydrate & Persist stay thin methods on `World` (same DB) — no `PlayerStore` type yet. Gameplay decides; Protocol transmits; Packets serialize.

**Why:** H1#8 needs a clear native World architecture before BDS. Flat-baked into `World` blocked gen without a storage rewrite. Folder explosion / `Item/` cut still deferred (§55).

**Non-goals:** BDS backend/converter (§61); noise product; `World/Terrain/` subfolders; `Item/` cut; rewriting overlays into subchunks. (**Dimension type** → shipped as seam in §71 — not multi-dim product.)

**Status (jul 2026):** Shipped — ADR + `WorldStorageKeys` + `ITerrainProvider` / `FlatTerrainProvider`. Product terrain gen → §63. Dimension seam → §71.

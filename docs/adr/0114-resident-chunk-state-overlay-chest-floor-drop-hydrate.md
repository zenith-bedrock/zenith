# ADR §114 — Resident chunk state (overlay/chest/floor-drop hydrate + evict) — design only, not shipped

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §114`.

**Choice:** Record a design-only ADR for bounding the RAM growth of `_blockOverrides`/`ChestStore`/
`FloorDropStore` (loaded entirely at boot today, never evicted — ADR §36's `SoftCap` only blocks
*new* keys). No code ships with this entry.

**Why:** the 2026-08-16 architecture audit flagged this as the most likely long-uptime operational
risk among its findings. A naive fix (evict a chunk's overlays when its last viewer leaves, mirroring
`_baseColumnCache`'s existing FIFO eviction or dragonfly's `closeUnusedChunks`) would silently drop
unpersisted player edits, because — unlike `_baseColumnCache`'s regenerable terrain — nothing loads
overlays/chests back in per-chunk today; everything is bulk-loaded once at boot. Hydrate has to exist
before evict is safe. This is design work, not urgent implementation, at alpha scale/uptime.

See [`docs/adr/0114-resident-chunk-state.md`](0114-resident-chunk-state.md) for the full
hydrate → resident → flush → evict lifecycle design, the `IChunkStorage` API gap
(`LoadOverlaysForChunkAsync`/`LoadChestsForChunkAsync` don't exist yet), and the open question
(viewer-count aggregation vs. periodic GC sweep) left for whoever implements this.

**Status (ago 2026):** Design recorded, not implemented. `World.cs`/`IChunkStorage.cs` unchanged.

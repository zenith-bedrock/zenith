# ADR §63 — Noise heightmap terrain (H1#8 product)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §63`.

**Choice:** First native gen after §62 seams — **seeded hash heightmap**, not biomes/caves/BDS.

1. **`OverworldTerrainSampler`** — shared surface Y + stone/grass/air rules; `SampleBlock` must match column build.
2. **`ChunkPayloads.BuildOverworldColumn`** — paletted subchunks from a world-space sampler (flat reuses it).
3. **`NoiseTerrainProvider`** — implements `ITerrainProvider`; per-chunk columns on miss (§45 no Put).
4. **Config:** `world.terrain: flat | noise`, `world.seed` (default `1`). Boot log prints mode.

| Mode | Provider | Surface |
|------|----------|---------|
| `flat` (default) | `FlatTerrainProvider` | constant Y -61 |
| `noise` | `NoiseTerrainProvider` | §63 hills → **§64** full overworld band + features |

**Storage interaction:** Existing `c:` blobs with valid subchunk v8 header still win (§45). Switching terrain on a populated world does not rewrite disk — operator creates fresh world folder or clears `c:` keys.

**Why:** H1#8 product step with smallest leaf surface; proves §62 seam before biomes or §61 import.

**Non-goals:** biomes, caves, ore, structures, multi-subchunk product beyond what hills need; persisting generated `c:` on miss; Mojang folder layout.

**Status (jul 2026):** Shipped — noise provider + config + leaf tests. Expanded band/features → §64.

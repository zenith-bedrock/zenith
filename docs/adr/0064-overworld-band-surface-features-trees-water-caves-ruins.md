# ADR §64 — Overworld band + surface features (trees, water, caves, ruins)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §64`.

**Choice:** Grow `noise` mode toward a Bedrock-shaped overworld **without** a biome engine or BDS gen.

1. **Height band:** surface ≈ Y 40–88 (base 64 ± hills), floor Y -64 bedrock, deepslate below Y 0, stone above.
2. **Sea:** `SeaLevel = 62` — air between surface and sea fills with water (lakes/coast).
3. **Caves:** deterministic 3D hash carve under surface (not noise libraries).
4. **Trees:** oak log + leaves on a cell grid (~22% of 10×10 cells), dry land only.
5. **Ruins:** rare 5×5 cobble pads + plank ring (structure placeholder — not villages).
6. **Spawn:** climb clear air above `max(surface, sea)` so trees/water never bury join/respawn.

Flat mode **unchanged** (classic Y -61). Features live only in `SampleNoiseBlock` / `NoiseTerrainProvider`.

**Why:** H1#8 “world beyond flat” needs playable vertical space and visible variety before biomes.

**Non-goals:** biomes, ore veins, villages/strongholds, mob spawning, structure NBT, liquid flow simulation, rewriting stored `c:` columns.

**Status (jul 2026):** Shipped — sampler expansion + Blocks (leaves/bedrock/water/cobble/deepslate) + tests.

**Adendo (jul 2026 — continuity):** Cross-chunk tree canopy: `BuildNoiseOverworldColumn` raises `maxWorldY` from neighbor canopy (`MaxTreeCanopyYAffectingChunk`) so leaves are not truncated at chunk borders. **Height algorithm** → superseded by Simplex in §71 (was 8×8 bilinear + blended biome bias).

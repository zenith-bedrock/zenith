# ADR §69 — Noise column gen — spatial caves + single-pass payload (join perf)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §69`.

**Choice:** Keep §65–§67 **geometry** (same hashes / worms / ores / biomes); speed PreSpawn via data-oriented column build.

1. **`OverworldCaveContext` XZ CSR grid** (8³ cells over 3×3 chunk neighborhood) — carve tests scan nearby segments only; brute-force path kept for leaf equality tests.
2. **`ChunkPayloads.BuildNoiseOverworldColumn`** — stack `FillColumnSurfaces` (256 surface Y + biome), single-pass sections to `max(surface+8, sea)`, `ArrayPool` section buffer, linear stack palette (no `Dictionary`).
3. **`SampleNoiseBlockAtSurface`** — column path reuses cached surface/biome; point `SampleBaseBlock` unchanged semantically.
4. **PreSpawn** still parallel `GetRadiusAsync` (§14 adendo).

**Why:** Linear segment scan × every underground block made radius-4 join ~50s+; spatial + no double-sample unlocks smoke join budget.

**Non-goals:** Changing worm counts/lengths, noise libs, biome blending, persisting `c:` on miss, SIMD mandatory.

**Status (jul 2026):** Shipped — cave grid + noise column builder + budget test.

# ADR §71 — Dimension seam + Simplex overworld (PM-inspired)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §71`.

**Choice:** Introduce a minimal **Dimension** domain type (Serenity/DF-shaped) and attach PM-inspired **Simplex** height/biomes to the overworld generator — without copying Serenity Entity/Feature maps or PM populate-with-adjacents.

1. **`Dimension`** — `Identifier`, `WireId` (= `Packets.DimensionId`), `ITerrainProvider Terrain`. `World` owns default **overworld**; LevelChunk / `ChunkColumnData.DimensionId` come from `World.Overworld.WireId`.
2. **`SimplexNoise`** leaf under `World/Noise/` (2D + octaves; no NuGet) — PM `Simplex`/`Noise` shape.
3. **Height** — octaved Simplex × `NoiseHillAmplitude` + biome bias (replaces 8×8 hash lattice).
4. **Biomes** — temp/rainfall Simplex → lookup mapped onto Ocean/Plains/Desert/Hills/Forest (replaces 48×48 hash cells).
5. Worms / ores / trees / column pipeline / join contract (§70) unchanged semantically.

**Supersedes:** §62 non-goal “Dimension type” (seam only — not multi-dim product). Clarifies §63–§67 height/biome algorithms.

**Why:** Wire already has DimensionId; gen/storage/spawn must not stay forever glued to an implicit mono-world. Better relief needs real noise, not hash lattices. Serenity proves World→Dimension→Generator; PM Normal proves Simplex climate+height.

**Non-goals:** Nether/End Dimensions; Serenity chunk GC / Entity maps / DimensionFeature; PM `PopulationUtils` 3×3 populate; noise NuGet; rewriting stored `c:` columns.

**Status (jul 2026):** Shipped — Dimension + Simplex overworld + leaf tests. **Noise leaf** → superseded by Auburn FastNoiseLite in §72 (height composition also hardened).

# ADR §137 — Ground-cover vegetation (short grass/flowers): a per-column probability roll, not a cell grid — and a new "passable decoration" block class

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §137`.

**Context:** requested improvement to world-gen, researched against the live Minecraft wiki (biome
temperature/downfall/surface-block table, tall-grass density ~8.6/chunk in Plains) and the local
reference server checkouts at `D:\Development\bedrock\`. Dragonfly's generator is a flat-world stub
with nothing to reference. PowerNukkitX has a fuller vanilla-accurate feature system but its
grass/flower features weren't found wired into any live biome→feature list in this checkout — design
reference only. PocketMine's `TallGrass` populator was the clearest, fully-wired model: one
`Populator` per biome, `baseAmount` attempts per chunk (Plains 12/256 cells, Forest 3, Mountains 1),
each attempt placing on the highest workable (air-above, grass-below) column via a per-chunk RNG
reseeded from `chunkX`/`chunkZ`/world seed. Its `growGrass()` flower algorithm (used for bonemeal, not
chunk-gen in this checkout) weights placement 4 short-grass : 1 dandelion : 1 poppy.

**Choice:** `OverworldTerrainSampler` gained a fourth deterministic pass, `SampleGroundCover`, checked
exactly once per column at `y == surface + 1` (after the existing tree/ruin feature check returns
air) — not folded into `SampleFeature`'s per-block scan, which already runs at every Y above the
surface and is this subsystem's documented performance bottleneck (`docs/world-generation.md`'s
Performance model). Unlike trees (which need a cell grid so canopies don't overlap), ground cover has
no spacing requirement between neighbors, so it's one independent `Hash(x, z, seed ^ salt)` roll per
column — no cell-anchor scan needed. `OverworldBiomeSampler.GroundCoverDensity` gives the roll's 1-in-N
odds per biome (Plains 1/16, Forest 1/48, Hills 1/96 — same order of magnitude as PocketMine's
attempts-per-chunk figures, not identical, since Zenith's model has no attempt-collision to account
for). Eligible biomes (`AllowsGroundCover`) are the same set as `AllowsTrees` today (Plains/Forest/
Hills) — Desert/Ocean get neither, matching vanilla's sparse/aquatic vegetation being a different,
not-yet-built block family (cactus, dead bush, kelp — none registered in Zenith yet). A hit picks
short grass, dandelion, or poppy at PocketMine's 4:1:1 weighting. Both the single-block
(`SampleNoiseBlock`) and column-build (`ChunkPayloads` via `FeaturePlacementPlan`) paths call the same
pure function, so the dual-sample contract (ADR §69) holds by construction without needing a
`FeaturePlacementPlan` entry for this feature.

**The block-solidity gap this surfaced:** `Blocks.IsSolid` was `!IsAir && !IsFluid` — every
previously-registered block genuinely was fully solid, so this was correct by coincidence, not by
a real classification. The moment a thin, walkable decoration block was going to exist, that
definition would have made every grass tuft an impassable wall. `Blocks` gained a third exclusion
category, `IsPassableDecoration` (a `_passableIds` set, same shape as `_fluidIds`/`_gravityIds`), and
`IsSolid` now also excludes it. `ShortGrass`/`Dandelion`/`Poppy` are the first (and currently only)
members. `DigProfiles` also gained entries for all three (`DestroySpeed 0.1` — the fastest nonzero
value this table uses, since `BreakDuration` divides by it; vanilla's real hardness is 0.0/instant,
0.1 is indistinguishable from instant at 20 TPS) — this table's own contract is "missing entry =
Survival cannot dig," so skipping registration would have made every placed tuft/flower permanently
unbreakable, not just cosmetically wrong.

**Non-goals:** no `BlockLoot` entry — the existing "no entry = drops the block itself" default is
already correct for flowers and an acceptable simplification for grass (real vanilla sometimes drops
wheat seeds instead; not modeled). No player placement (`_placeableIds` unchanged) — a player can
break and hold these items but not replant them yet, a smaller, separable follow-up. No expansion of
the biome catalog itself (still Ocean/Plains/Desert/Hills/Forest) — real vanilla's dozen-plus biomes
(Taiga, Jungle, Savanna, Swamp, Badlands, snowy biomes, Mushroom Fields, …) each need their own new
block types (spruce/birch/jungle wood, snow, terracotta, mycelium, cactus, dead bush) that don't exist
in Zenith's palette yet — a substantially larger, separately-scoped undertaking, not attempted here.
No 2-tall `tall_grass` variant (Bedrock's newer flat `short_grass` is single-block, avoiding the
2-cell placement/state complexity `tall_grass` would add for comparatively little visual gain at this
scope). No aquatic vegetation (kelp/seagrass) or dry-biome vegetation (cactus/dead bush) — Desert/Ocean
get no ground cover this pass.

**Status (ago 2026):** Shipped — `Blocks.cs`, `DigProfiles.cs`, `OverworldBiomeSampler.cs`,
`OverworldTerrainSampler.cs`.

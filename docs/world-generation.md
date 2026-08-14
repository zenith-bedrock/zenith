# Overworld world generation

Living document — describes the current implementation, not a phase diary. See
`docs/history/phases/phase-xxiv-overworld-generation-findings.md` for what changed and why during
the audit that produced this doc.

## Provider boundary

```
ITerrainProvider
    decides deterministic base terrain — GetBaseColumn (full 16×16 column) and
    SampleBaseBlock (single block) — the two MUST always agree; see "Dual-sample contract" below.

World
    owns player modifications (block overrides), keyed independently of the provider.
    GetBlock = overlay if present, else SampleBaseBlock.

IChunkStorage (InMemoryChunkStorage / LevelDbChunkStorage)
    persists overlays, chests, inventory, player data, and world identity (seed/terrain mode).
    Does NOT persist generated base terrain — see "Base terrain is never cached to disk" below.

ChunkPayloads / Protocol
    project TerrainColumn/World state to the Bedrock wire (paletted subchunks, protocol 2168).
```

Two implementations of `ITerrainProvider` exist: `FlatTerrainProvider` (classic stone/grass column,
still the config default) and `NoiseTerrainProvider` (deterministic procedural Overworld — the
subject of this document). Select via `world.terrain: flat|noise` in `zenith.yml`; `TerrainProviders.Create`
is the only place that switches on the string.

## Seed contract

The seed is **world identity**, not a per-restart config knob. `WorldIdentity.Reconcile` (called once
at boot, `src/zenith/Server/ZenithServer.cs`) enforces this:

- A brand-new world (no `wm:` metadata key yet) commits whatever `zenith.yml` currently says
  (`world.terrain` + `world.seed`) as its permanent identity.
- Every later boot reads the committed identity back and uses it — **not** the live config — if they
  disagree. A mismatch is logged loudly as a misconfiguration; the world's original identity always
  wins, because generation never persists base chunks (next section) and a silently-changed seed would
  make every un-visited chunk regenerate as a different world sitting next to already-explored terrain.

To intentionally start over with a different seed, use a new `world.path`/`world.name`.

## Base terrain is never cached to disk

`World.GetOrCreateColumnAsync` only writes a `c:` LevelDB entry in two cases: never on an ordinary
miss (ADR §45 — "miss → in-memory base only"), and only to self-heal a legacy/corrupt stored payload.
This means **every chunk a player hasn't overlaid gets regenerated from the provider on every load**,
always from the current committed seed. This is why the seed contract above is load-bearing, not
just tidy: it's the only thing keeping regeneration consistent across restarts.

## Dual-sample contract (the load-bearing invariant)

`ITerrainProvider.SampleBaseBlock(x, y, z)` (single block) and `GetBaseColumn(chunkX, chunkZ)` (full
column, used for both the wire payload and, per the point above, every reload) **must never disagree**
for the same coordinates. For `NoiseTerrainProvider` this is enforced by construction, not by
discipline: both paths funnel through the same `OverworldTerrainSampler.SampleNoiseBlockAtSurface`,
and the column path passes it the same cached `surface`/`biome` values `SampleBaseBlock`'s path (via
`SurfaceY`/`SampleKind`) would compute independently. The only parameter that differs between the two
call sites is the cave lookup: the column path passes a prebuilt `OverworldCaveContext` (see below);
the single-block path builds an equivalent context on the fly via `OverworldCaveCarver.IsCarved`.
`TerrainProviderTests.Noise_GetBlock_matches_SampleBaseBlock_including_features` and
`CaveCarverTests`/`OrePlacerTests`' `Column_build_matches_SampleBaseBlock_with_*` variants exercise
this directly; `WorldGenOrderAndSeamTests.Cave_carve_state_agrees_across_every_context_that_should_see_it`
additionally proves the two cave paths agree cell-by-cell, not just in aggregate.

## Generation pipeline

```
seed
 │
 ├── OverworldNoiseFields.For(seed)   — cached per-seed FastNoiseLite fields (Height/Temperature/Rainfall)
 │
 ├── OverworldBiomeSampler
 │      SampleClimate → Lookup(temperature, rainfall) → OverworldBiomeKind
 │      (Ocean / Plains / Desert / Hills / Forest — five coarse kinds, IDs match Bedrock network biome ids)
 │
 ├── OverworldTerrainSampler
 │      FillSurfaceAt: height noise + ContinuousHeightBias(biome) → surface Y (clamped [-52, 120])
 │      SampleNoiseBlockAtSurface: surface/subsurface material, water fill to SeaLevel (63),
 │        bedrock floor, deepslate below Y0, trees, ruins, delegates to OverworldOrePlacer for
 │        stone/deepslate replacement
 │
 ├── OverworldCaveCarver / OverworldCaveContext
 │      deterministic worm + cheese-sphere carving, world-coordinate hashed (not per-chunk RNG)
 │
 ├── OverworldOrePlacer
 │      deterministic vein clusters, host-gated (stone/deepslate only)
 │
 └── ChunkPayloads.BuildNoiseOverworldColumn
        one pass per column: cache surface/biome once (FillColumnSurfaces), build one
        OverworldCaveContext for the 3×3 chunk neighborhood, then sample every block via the
        same SampleNoiseBlockAtSurface the single-block path uses → paletted subchunks → wire payload
```

Every generation input is a pure function of `(worldX, worldY, worldZ, seed)` (or a cell derived from
those coordinates via `FloorDiv`, never chunk-generation order). No mutable global RNG state
(`Random.Shared`, `GetHashCode()`) is used anywhere in the pipeline — all hashing goes through the
local `Hash`/`Hash3` functions in each file, which are pure and stable across process restarts.

## Biomes

Five coarse kinds (`OverworldBiomeKind`): Ocean, Plains, Desert, Hills, Forest. Classification is a
temperature/rainfall lookup (`OverworldBiomeSampler.Lookup`, shaped after PocketMine's Normal biome
selector, condensed onto five buckets). `ContinuousHeightBias` blends height by climate weight (not a
hard per-biome `Min`/`Max`) specifically to avoid 1-block cliffs at biome edges. Surface material:
Desert and Ocean get Sand/Sand; everything else gets GrassBlock/Dirt (subsurface). Biome distribution
was measured across three seeds at 2048×2048-block scale — no biome ever dominates (<85%) or fails to
appear; see the phase findings doc for the actual percentages.

## Sea level and water

`SeaLevel = 63` (vanilla). Any column cell above the terrain surface and at or below sea level fills
with water; no separate ocean generator exists — ocean coherence comes entirely from the climate
model's `oceanW` height-bias term pulling surface well below 63 across a whole climate region, not a
per-block decision.

## Caves

Two carve types, both in `OverworldCaveCarver`: "worms" (2-4 per chunk, 48-128 step random walks,
radius 1-3 depending on depth) and an occasional single "cheese" sphere. `OverworldCaveContext`
precomputes segments for a 3×3 chunk neighborhood once per column build (spatial-bucketed for O(nearby)
point queries) rather than re-deriving per block.

**Containment invariant (Phase XXIV fix):** every worm segment is clamped to stay within one
chunk-width (plus max carve radius) of its own origin chunk. Without this, a worm could wander far
enough (up to 128 unclamped steps) that a query chunk more than one chunk away from the worm's origin
would never re-include it — the tunnel would silently vanish at an arbitrary point that had nothing to
do with the worm's own path. See `OverworldCaveCarver.WormMaxRadiusMargin`'s doc comment.

`SurfaceGuardDepth = 4` blocks carving within 4 blocks of the surface, preventing caves from breaching
straight through the surface layer at a visible pit.

## Ores

`OverworldOrePlacer.TryReplaceHost` only ever replaces Stone or Deepslate hosts, via 8-block vein
cells (Chebyshev radius 2, ~1-in-7 chance a cell has a vein at all). Vertical bands (corrected against
an independent reference server's vanilla-faithful generator during Phase XXIV):

| Ore | Band | Notes |
|---|---|---|
| Coal | y ≥ 0 | previously "any y", including diamond depth — real bug, fixed |
| Copper | y ≤ 112 | |
| Iron | y ≤ 64 **or** 80-120 | added the shallow band; previously single-band only |
| Gold | y ≤ 32 | |
| Redstone | y ≤ 16 | |
| Lapis | -32 ≤ y ≤ 32 | already correct |
| Diamond | y ≤ -4 | previously y ≤ 16 (far too shallow) — real bug, fixed |

Deepslate vs. regular ore variant is picked by the *queried block's* Y region, not the vein's origin
cell — so a single vein straddling the Y0 stone/deepslate transition correctly shows both variants,
matching vanilla. `DeepslateCoalOre` is currently unreachable under Zenith's simplified hard Y0
transition (coal's y≥0 band never overlaps the deepslate side) — a known, accepted simplification of
the deepslate/stone cutoff being a hard point rather than vanilla's 0-8 probabilistic gradient.

## Trees, ruins, and feature ordering

Trees: `TreeCellSize = 10`, one candidate anchor per cell (`TryTreeAnchor`), trunk height 4-6, canopy
radius 2. Eligible in Plains/Forest/Hills only, with per-biome density odds
(`OverworldBiomeSampler.TryTreeRoll`). Sampling scans the 3×3 neighborhood of tree cells around the
queried block so a trunk anchored in one chunk correctly paints leaves into an adjacent chunk — this
falls out of being purely world-coordinate-based, no special cross-chunk casing needed.

Ruins: `RuinCellSize = 40`, one candidate per cell, ~1-in-11 chance, small 5×5 cobblestone/plank
footprint. Anchor offset is bounded well inside its own cell, so a ruin never needs neighbor-cell
scanning to stay correct across chunk boundaries (unlike trees) — it's just a pure function of world
coordinates like everything else.

Ordering (`SampleFeature`, called from `SampleNoiseBlockAtSurface`): **trees are checked before
ruins, and both are checked before ore** at any given block above the deepslate zone — a tree log/leaf
"wins" over what would otherwise be an ore-bearing stone block at the same position, since the feature
check runs first and short-circuits. Ore is only tried once the feature check returns air. Caves are
resolved even earlier (before surface/subsurface/feature are checked at all), so a carved cell is
always air regardless of what a tree/ruin/ore rule would have put there.

## Spawn safety

`SampleSpawnFeetY` scans upward from `max(surfaceY, SeaLevel) + 1` for the first candidate where feet
and head cells are air. Phase XXIV added a third requirement: the cell directly underfoot must be
non-air (solid support), matching an independent reference server's spawn-safety check — the
air-only version could place a player floating above e.g. leaf canopy or a carved void with nothing
solid supporting them.

## Vertical bounds

World spans Y -64 to 319 inclusive (`Blocks.FlatMinY = -64`; sampler/payload code bounds-check against
320). Confirmed against reference implementations to match current Bedrock's expanded world height —
no change needed. Surface itself is clamped to [-52, 120] (`Blocks.FlatMinY + 12` to `120`); nothing in
the pipeline currently produces terrain outside that band.

## Performance model

Real measurement (Release, `dotnet run --project src/zenith.Benchmarks -c Release -- -f "*WorldgenColumn*"`,
see the phase findings doc for the full table): a single `NoiseTerrainProvider.GetBaseColumn` call
costs **~48.6ms** — most of a 50ms GameLoop tick budget, if it ran on the tick thread. `OverworldCaveContext`
construction alone (`Noise_CaveContextOnly`) is only ~64μs — under 0.15% of the total — so caves are
not the bottleneck; the per-block feature (tree/ruin) scan across every stone/deepslate block in the
column is the leading suspect but wasn't isolated further this pass (see findings doc's "not yet
optimized" note for why a naive depth-based bail was rejected as unsafe).

**This does not currently stall the GameLoop.** Column generation is only ever invoked from
`ChunkStreamSystem`'s fire-and-forget `async Task` methods (`StreamAsync`/`LoadPreSpawnAsync`), after
an `await` on `IChunkStorage`. For `LevelDbChunkStorage` (the production path) that read is genuinely
asynchronous (`Task.Run`-backed), so the continuation — including the expensive `GetBaseColumn` call —
resumes on a ThreadPool thread, never the GameLoop thread, with no `SynchronizationContext` to marshal
it back. The async/apply boundary this project's architecture wants (compute off-thread → immutable
result → apply on the authoritative loop) already exists here, for free, as a side effect of the
storage layer being async — no dedicated worldgen job system was built or is currently justified.
`InMemoryChunkStorage` (dev/test only — used when no `world.path`/`ZENITH_DATA` is configured) does
NOT provide this decoupling, since its reads complete synchronously; this is an accepted gap for a
mode that's explicitly ephemeral/non-production.

## Known simplifications (intentional, not oversights)

- Deepslate/stone transition is a hard cutoff at Y0, not vanilla's Y0-8 probabilistic gradient.
- Only two surface-material pairs exist across five biomes (Sand/Sand for Desert+Ocean,
  Grass/Dirt for everything else) — no biome-specific `seaFloorBlock` beyond the Ocean case, no
  gravel, no biome-specific subsurface variation.
- No vegetation beyond trees (no flowers/grass/mushrooms).
- Ruins are a single fixed small shape, not a structure catalog.
- No aquifers; a cave carved under an Ocean biome is a dry air pocket, not flooded or specially
  sealed — no pathological outcome was found in this pass, but it also wasn't stress-tested visually.
- No mob spawning integration — `OverworldTerrainSampler`/`OverworldBiomeSampler` expose everything a
  future spawner would need (surface Y, biome, solid/air queries) without any spawning-specific API.
- No lighting engine changes were made or are believed necessary this phase.

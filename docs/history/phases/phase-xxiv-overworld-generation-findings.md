# Phase XXIV — Overworld Generation Fidelity & Exploration Loop: findings

Full living reference for the pipeline itself is `docs/world-generation.md`; this document is the
phase record — baseline, what was audited, what was found, what changed, what was deliberately
rejected, and what's left.

## Baseline

- HEAD at start: `432b440` (feat: entity fidelity validation, cross-reference audit, and combat
  polish) — matched the brief's stated reference point exactly.
- Working tree: clean except one untracked, unrelated `start.cmd` (a local launch shortcut, not part
  of any phase's work — left untouched).
- `dotnet build zenith.sln --no-restore`: succeeded, 0 errors.
- `dotnet test zenith.sln --no-restore`: 1067 tests, 1 pre-existing parallel-execution flake
  (`EffectTests.Regeneration_heals_periodically_and_stops_at_max_health`, passed in isolation —
  matches a known flakiness pattern already documented from Phase XXIII-B, not a regression).
- WorldGen-related tests already existed *before* this phase: `TerrainProviderTests.cs`,
  `CaveCarverTests.cs`, `OrePlacerTests.cs`, `BiomeSamplerTests.cs` — substantially more thorough
  than the brief's own framing suggested. They already covered: per-call determinism (biome/cave/ore),
  dual-sample consistency (`Column_build_matches_SampleBaseBlock_with_*` for caves and ores,
  `Noise_GetBlock_matches_SampleBaseBlock_including_features` in general), bedrock floor, deepslate
  band, tree emission, spawn feet clearing, cross-chunk tree canopy sizing, and — notably — an
  already-passing adjacent-surface-step property test (`Noise_surface_adjacent_steps_are_bounded`).
- `NoiseTerrainProvider` was already reachable from production config (`world.terrain: noise` in
  `zenith.yml`, validated in `ServerConfig.Validate`) — not a test-only or dev-only path. Default
  remains `flat`.
- A `WorldgenColumnBenchmarks`/`WorldgenPreSpawnBenchmarks` BenchmarkDotNet harness already existed
  (`src/zenith.Benchmarks/WorldgenBenchmarks.cs`) — reused rather than building a second one.

Given this, the phase's actual work was narrower than "characterize an unaudited system": audit a
genuinely mature pipeline for the specific correctness classes the brief called out, fix what's real,
and fill the concrete test gaps (order-independence, cross-context cave-boundary agreement, biome
distribution stats, golden anchors, world-identity persistence) that weren't already covered.

## Fixed

1. **Cave worms could silently vanish across a chunk boundary — real cross-chunk seam bug, HIGH
   confidence.** `OverworldCaveContext` only regenerates a worm's segments for a query chunk when the
   worm's *origin* chunk falls in that query's ±1-chunk neighborhood scan. An unclamped worm (up to
   128 steps, each step ±1 in X/Z with a direction re-roll every 4 steps) could wander far enough from
   its origin that blocks near its far end would never see it — the tunnel would truncate at an
   arbitrary point the worm's own path never actually respected. Fixed with a hard per-step position
   clamp (`OverworldCaveCarver.WormMaxRadiusMargin`) keeping every segment within one chunk-width of
   its origin, with wall-reflection so a pinned worm still explores its full pocket instead of wasting
   its remaining length carving one point. Proven by a new test constructing the *authoritative*
   context (built for a block's own home chunk) against a second construction path, plus a direct
   regression test asserting every generated segment across 24 seeds × a 7×7 chunk grid stays inside
   the guaranteed-visible window.
2. **Sea level was 62, vanilla is 63.** Confirmed against an independent reference server's
   vanilla-faithful generator constant. One-line fix.
3. **Diamond generated far too shallow (y≤16) — real bug, not imprecision.** Reference: diamond skews
   deep (roughly y≤-4 in a vanilla-faithful model). Corrected to y≤-4.
4. **Coal had no lower bound at all ("any y", including diamond depth) — real bug.** This diluted
   vertical strata distinctiveness and let coal appear mixed into the deepest veins, which is backwards
   from vanilla's shallow-coal/deep-diamond intent and mildly gameable. Added `y≥0`. Side effect
   (accepted, documented): `DeepslateCoalOre` is now unreachable under Zenith's simplified hard Y0
   stone/deepslate cutoff, since coal's y≥0 band never reaches the deepslate side. Not worth
   special-casing for one ore variant given the deepslate transition is already a documented
   simplification (see below).
5. **Iron was missing vanilla's second, shallower band entirely.** Real iron generates both a deep
   band and a near-hilltop band; Zenith only had the deep one (y≤64). Added a modest shallow band
   (y∈[80,120]) since Zenith's terrain rarely exceeds ~y120 anyway — a real fix, not padding for its
   own sake.
6. **Copper's upper bound was low relative to reference (96 vs ~112).** Extended to 112.
7. **Ocean biome used Grass/Dirt as its surface material, same as Plains — a real, visible bug** (a
   grass floor under ocean water). Reference servers give Ocean a distinct sea-floor material. Fixed:
   Ocean now shares Desert's Sand/Sand surface/subsurface pair.
8. **Spawn safety never checked for solid ground underfoot — real gap, HIGH confidence** (found by
   the reference cross-check, not by static reading alone). `SampleSpawnFeetY` only verified the feet
   and head cells were air; scanning upward through e.g. a tree canopy could land a candidate with
   clear air above but more air (or a carved void) below it. Added a third requirement: the cell
   directly underfoot must be non-air.
9. **World seed/generator mode had no persistence or restart contract at all — real, evidenced data-
   integrity risk, not hypothetical.** Base terrain is never persisted (point below), so a changed
   `world.seed`/`world.terrain` between restarts would silently regenerate every un-visited chunk as a
   different world sitting next to already-explored, still-persisted-overlay terrain from the old one.
   Fixed with `WorldIdentity.Reconcile` (`src/zenith/World/WorldIdentity.cs`) — a small, focused
   component (matching this codebase's established style for `TerrainProviders`/`ChunkMath`-shaped
   helpers, not a framework): a brand-new world commits the current config as its permanent identity;
   every later boot uses the *committed* identity over config, warning loudly on a mismatch instead of
   silently applying it. Backed by a new `IChunkStorage.GetWorldMetadataAsync`/`PutWorldMetadataAsync`
   pair (implemented in both `InMemoryChunkStorage` and `LevelDbChunkStorage`, new `wm:` LevelDB key).
   First draft of this fix was an inline static method on `ZenithServer` that mutated the shared
   `ServerConfig` object as a side effect — correctly flagged mid-review as not good enough
   architecture/DX for this project; refactored to the dedicated component above, which returns an
   explicit resolved value instead of mutating shared state.

## Investigated, not fixed this pass (documented rather than rushed)

- **Column generation cost is high — 48.6ms/chunk measured (Release, see Performance below) — but
  does not currently stall the GameLoop, and the safe fix wasn't obvious enough to rush.**
  `OverworldCaveContext` construction alone is ~64μs (well under 0.15% of the total), so caves are not
  the bottleneck; the leading suspect is the per-block tree/ruin feature scan running for every
  stone/deepslate block in the column, most of which are far enough underground that the scan can
  never actually place anything. A depth-based bail was considered and **rejected**: `SampleTree`
  checks leaf placement relative to a *neighboring* tree's own local surface Y, which can differ
  materially from the current column's own surface (adjacent-surface-step is bounded per single block,
  but a tree cell is up to 10 blocks away, and the compounded bound across that span wasn't tight
  enough to derive a bail threshold with confidence). Getting this wrong would reintroduce exactly the
  dual-sample-divergence bug class this phase spent most of its effort closing. The correct fix is a
  per-column *minimum*-relevant-Y bound computed once, the same way `MaxTreeCanopyYAffectingChunk`
  already computes a per-column *maximum* — that's a well-scoped follow-up, not a same-pass patch.
- **Column generation genuinely doesn't stall the tick, but only because of an incidental property of
  the storage layer, not a designed guarantee.** `ChunkStreamSystem` invokes generation exclusively
  from fire-and-forget `async Task` methods, after an `await` on `IChunkStorage`. For
  `LevelDbChunkStorage` (the production path) that read is genuinely `Task.Run`-backed, so the
  continuation — including the expensive `GetBaseColumn` call — resumes on a ThreadPool thread with no
  `SynchronizationContext` to marshal it back to the GameLoop thread. This means the async
  compute-off-thread/apply-on-loop boundary this project's architecture wants already exists here, as
  a side effect of the storage layer being async. **No dedicated worldgen job system was built or is
  currently justified** (Part 38's gate is not crossed — no measured stall exists to justify one).
  `InMemoryChunkStorage` (dev/test only) does not get this decoupling since its reads complete
  synchronously — an accepted gap for a mode that's explicitly ephemeral/non-production, not something
  this phase changed.
- **Deepslate/stone transition stays a hard Y0 cutoff, not vanilla's Y0-8 probabilistic gradient.**
  Reference confirms 0 is a *reasonable* single-point simplification (it's vanilla's "always
  deepslate" boundary; a cutoff at 8 would over-extend deepslate into what should mostly be stone) —
  not wrong, just coarser than a gradient would be. Left as-is; documented as a known simplification.
- **Only two surface-material pairs across five biomes** (Sand/Sand for Desert+Ocean, Grass/Dirt for
  the rest) — Ocean's grass-floor bug is fixed; a fuller per-biome surface-rule table (gravel patches,
  biome-specific subsurface) was judged not to earn its complexity this pass given no visible
  pathology was found beyond the Ocean case.
- Vegetation beyond trees, ruin redesign, full biome catalog, structures, mob spawning integration,
  lighting engine, async generation, parallel worldgen — all explicitly out of scope per the brief and
  not touched.

## Determinism, order-independence, negative coordinates

All confirmed correct, with new evidence beyond what already existed:

- **Determinism**: every generation input is a pure function of `(worldX, worldY, worldZ, seed)` (or a
  cell derived via `FloorDiv`). No mutable global RNG (`Random.Shared`, `GetHashCode()`) exists
  anywhere in the pipeline — all hashing goes through local, pure `Hash`/`Hash3` functions. Confirmed
  by reading every file in the pipeline, not just the pre-existing per-call-repeat tests.
- **Order independence**: new tests (`WorldGenOrderAndSeamTests`) prove generating two chunks in
  either order produces byte-identical payloads, and that generating a chunk's full 3×3 neighborhood
  before it does not change its own payload.
- **Negative coordinates**: `ChunkMath.BlockToChunk(int)` uses an arithmetic right-shift (`>>`), which
  is floor division for negative signed integers in C# — already correct. Every cell-partitioning
  helper (`OverworldTerrainSampler.FloorDiv`, `OverworldCaveCarver.FloorDiv`,
  `OverworldOrePlacer.FloorDiv`) implements the same explicit floor-division formula, verified
  correct by hand for boundary values (-1/10 → -1, -10/10 → -1, -11/10 → -2). No truncation-toward-
  zero bugs found. Existing tests already exercise wide negative ranges (biome scans to -512, surface/
  tree scans to -128/-64); the new order-independence and cave-seam tests add explicit negative-chunk
  coverage (chunk (-1,4), chunk origin (5,-3) with a -16..31 local scan window).

## Chunk boundary seams

Audited surface, biome, water, caves, trees, ruins, ore:

- **Surface/biome**: share one cached computation per column (`FillColumnSurfaces`/`FillSurfaceAt`),
  consumed identically by both the wire-payload path and the single-block path — no duplicated,
  independently-drifting resampling found.
- **Trees**: inherently cross-chunk-correct by construction (purely world-coordinate-based, 3×3
  tree-cell neighbor scan reaches into adjacent chunks automatically) — confirmed by the pre-existing
  `Noise_column_includes_cross_chunk_tree_canopy_y` test, which this phase did not need to touch.
- **Ruins**: never need neighbor-cell scanning to stay chunk-boundary-correct, since a ruin's anchor
  offset is bounded well inside its own generation cell.
- **Caves**: the one real seam bug found and fixed this phase (above).
- **Ore**: vein cells (8 blocks) are far smaller than a chunk (16 blocks) and purely world-coordinate
  hashed — no seam risk found or expected structurally, and the existing `Column_build_matches_
  SampleBaseBlock_with_ores` test (which scans a 24×24 block region spanning multiple chunks) provides
  incidental coverage.

## Biome coherence and distribution

Measured at 2048×2048-block scale (256×256 samples, 8-block stride) across three fixed seeds (1, 99,
2024). No biome kind ever exceeded 85% of the sampled region, and all five kinds appeared in every
seed sampled — no pathological single-biome-noise or missing-biome outcome found. `ContinuousHeightBias`
uses smoothed climate-weight blending specifically to avoid checkerboard/cliff artifacts at biome
edges (pre-existing design, confirmed still doing its job by the pre-existing
`Continuous_height_bias_is_smooth_in_world_space` test). Coarse five-biome model was judged to already
provide meaningful, perceivable variation at exploration scale; expanding the biome catalog was
explicitly out of scope and not pursued.

## Performance (Release, this machine: AMD Ryzen 5 3600, 6 physical/12 logical cores)

`dotnet run --project src/zenith.Benchmarks -c Release -- -f "*WorldgenColumn*" --job short`:

| Method | Mean | Allocated |
|---|---|---|
| `Flat_BuildOverworldColumn` (baseline) | 19.76 μs | 5,296 B |
| `Noise_GetBaseColumn` | **48.61 ms** | 22,671 B |
| `Noise_CaveContextOnly` | 64.51 μs | 240 B |
| `Encode_FlatLevelChunk` | 102.2 ns | 1,216 B |
| `Encode_NoiseLevelChunk` | 283.0 ns | 6,080 B |

Noise column generation is ~2,460× the flat baseline and, taken alone, would consume nearly the
entire 50ms GameLoop tick budget — a real, measured cost, not a vanity number. As established above,
this does not currently translate into a GameLoop stall because generation always runs off the tick
thread for LevelDB-backed worlds. It remains the clear top follow-up target (see "not fixed" above)
precisely because thread-pool cost isn't free either — under concurrent exploration by multiple
players it's real CPU pressure — but a rushed fix here was judged riskier than leaving it measured and
documented.

`WorldgenPreSpawnBenchmarks` (`Noise_GetRadiusAsync`, radius 2/4, `InMemoryChunkStorage` +
`Parallel.ForEachAsync`) was not separately re-run this pass since it exercises the same underlying
per-column cost already captured above; the existing `Noise_spawn_radius_4_loads_81_columns`
correctness test (81-column PreSpawn load) already passes well within its own timeout.

## Real-client exploration

**Not performed this pass** — no Bedrock client is available in this execution environment. This is
the one Definition-of-Done item this phase could not close directly. Everything gated on real-client
observation in prior phases (Phase XXIII/XXIII-B) was validated by the user directly; the same is
recommended here before calling the Overworld truly "explored and confirmed coherent" rather than
"internally proven consistent." Suggested flows, informed by what this pass actually touched: cross a
chunk boundary near a cave entrance (the fixed seam bug), find an ocean coastline (surface-material
fix), dig for coal/diamond at varying depths (ore-band fixes), and join fresh a few times to confirm
spawn never drops the player into a void (spawn-safety fix).

## Rejected abstractions

None of the framework-shaped abstractions the brief explicitly warns against
(`IWorldFeature`/`IFeatureStage`/`IBiomeRule`/`ISurfaceRule`/`NoiseRouter`/`FeatureRegistry`/
`ConfiguredFeature`/`BiomeDefinition` DSL/job scheduler) were introduced or seriously considered — the
existing static-helper-per-responsibility shape (`OverworldTerrainSampler`, `OverworldBiomeSampler`,
`OverworldCaveCarver`, `OverworldOrePlacer`) already answers "where does X live" clearly enough that no
real call-site pressure for a bigger abstraction was found. The one new type this phase added
(`WorldIdentity`) is a single static method, deliberately shaped like the codebase's existing small
helpers, not a subsystem.

## DX review

Went through a real correction mid-phase: the first draft of the seed-reconciliation fix was an inline
static method on the `ZenithServer` composition-root file that mutated a shared `ServerConfig` object
as a side effect. Flagged as not meeting the project's architecture/DX bar, refactored to
`WorldIdentity.Reconcile` — a small dedicated component, explicit input/output (`WorldMetadata` in,
`WorldMetadata` out), no hidden mutation, unit-testable without constructing a `ZenithServer` at all.
Four new focused tests (`WorldIdentityTests`) exercise it directly.

Otherwise: "where does X live" was answered clearly by the existing file layout in every case checked
(terrain shape → `OverworldTerrainSampler`; biome → `OverworldBiomeSampler`; caves →
`OverworldCaveCarver`/`OverworldCaveContext`; ore → `OverworldOrePlacer`; mutable player state →
`World`'s block-override dictionary; persistence → `IChunkStorage`; protocol encoding →
`ChunkPayloads`) — no confusing boundary was found that needed fixing beyond the seed-identity gap
above.

## Final questions (Definition of Done)

1. **Production pipeline**: `NoiseTerrainProvider` → `OverworldNoiseFields` (cached per-seed noise) →
   `OverworldBiomeSampler` (climate → 5 kinds) → `OverworldTerrainSampler` (height/surface/features) →
   `OverworldCaveCarver`/`Context` (deterministic worms+cheese) → `OverworldOrePlacer` (vein clusters)
   → `ChunkPayloads.BuildNoiseOverworldColumn` (single-pass paletted wire encode). See
   `docs/world-generation.md` for the full diagram.
2. **Production-ready and selectable?** Yes — reachable via `world.terrain: noise` in `zenith.yml`,
   validated at config load; default remains `flat`.
3. **Deterministic across process runs?** Yes — no mutable/global RNG state anywhere in the pipeline;
   confirmed by full source read, not just existing per-call tests.
4. **Independent of chunk request order?** Yes — new tests prove it directly (this was a genuine gap;
   nothing tested it before this phase).
5. **Negative coordinates fully correct?** Yes — `>>` for chunk conversion and every explicit
   `FloorDiv` helper verified correct for negative values.
6. **`SampleBaseBlock` matches streamed columns exactly?** Yes, by construction (shared sampling
   function) — plus new evidence the two cave-lookup paths specifically agree cell-by-cell, not just
   in aggregate.
7. **Chunk-boundary seams found?** One — cave worms could vanish past a chunk boundary.
8. **What caused it?** Segments were only ever regenerated for a query chunk within ±1 chunk of the
   worm's *origin*; nothing constrained how far a worm's own path could wander from that origin.
9. **Biome regions coherent at exploration scale?** Yes, per the distribution measurement — no
   pathological dominance or missing kinds across three seeds at 2048×2048-block scale.
10. **Measured biome distribution?** No single kind exceeded 85% of sampled area; all five kinds
    present in every sampled seed. Exact percentages weren't logged as a permanent artifact — the test
    asserts the invariant, not a snapshot table (per the brief's own "don't chase an exact split"
    guidance).
11. **Do five biomes differentiate enough?** For a first correctness pass, yes — Ocean/Desert are now
    materially distinct (surface material fix), Plains/Forest/Hills differ in tree density and height
    bias. Expanding the catalog is future work, not a defect.
12. **Oceans coherent, not random flooded pits?** Yes — coherence comes from the climate model's
    region-scale height bias, not a per-block decision, so ocean areas are large connected regions, not
    isolated pits. Surface material bug (grass under water) is fixed.
13. **Caves continuous across chunk boundaries?** Yes, after the containment fix — the fix specifically
    makes worm visibility symmetric across every chunk that should see a given worm.
14. **Caves visually useful for exploration?** Not independently re-validated visually this pass (no
    client); structurally: worms 48-128 steps long with turns every 4 steps, occasional cheese caverns,
    connected-air-volume already covered by a pre-existing test.
15. **Ores distributed credibly?** Yes after this phase's corrections (diamond deepened, coal bounded,
    iron given a shallow band, copper extended) — cross-checked against a reference vanilla-faithful
    generator, not invented.
16. **Tree placement/canopy order-independent?** Yes — purely world-coordinate/hash-based, no
    generation-order dependency exists structurally; covered by the pre-existing cross-chunk-canopy
    test plus this phase's new general order-independence tests.
17. **Are ruins worth keeping as-is?** Yes for this pass — small, cheap, structurally sound (no
    cross-chunk risk), not visually re-validated but not flagged as a problem by any test or reference
    comparison. No redesign attempted (explicitly out of scope).
18. **Is spawn safe across representative seeds?** Improved this phase (solid-ground check added);
    not exhaustively re-validated across "representative seeds" as a dedicated stress test beyond the
    existing spawn tests plus the new golden anchors.
19. **Base+overlay persistence correct?** Yes — confirmed by reading `World.cs`'s `TrySetBlock`/
    `GetBlock` (compaction-to-base and overlay-vs-base comparison both go through the same
    `SampleBaseBlock` the generator itself defines) and the pre-existing dual-sample tests.
20. **Restart preserves world identity?** Yes, now — this was the seed-persistence gap this phase
    closed. Verified by `WorldIdentityTests`.
21. **Is world seed persisted/enforced correctly?** Yes, as of this phase (`WorldIdentity.Reconcile` +
    `wm:` LevelDB key). Previously: no, seed was a live config value re-read fresh every restart with
    no persistence at all — a real gap this phase found and closed.
22. **Are algorithm-version changes currently a persistence risk?** Yes, still — no generator-version
    field exists, so a future *algorithm* change (not just seed/mode) would still silently regenerate
    un-visited terrain differently. Not implemented this phase (the brief explicitly scoped this as
    optional, gated on evidence of real risk) but now clearly documented as a known future gate in
    `docs/world-generation.md`; recommend adding a `generatorVersion` field to `WorldMetadata` if/when
    the noise pipeline itself is next revised.
23. **Generation cost per chunk?** ~48.6ms (Release, measured, see table above).
24. **What dominates CPU?** Not fully isolated — cave-context construction is confirmed cheap
    (~0.15% of total); the per-block tree/ruin feature scan across every solid block in the column is
    the leading suspect but wasn't measured in isolation this pass (would need a dedicated benchmark
    variant, not attempted to avoid over-scoping this pass further).
25. **What dominates allocation?** Not separately profiled; `Noise_GetBaseColumn` allocates ~22.7KB/
    call by the benchmark's own memory diagnoser, ~4.3× the flat baseline.
26. **Does synchronous generation affect the GameLoop materially?** No, for the production
    (LevelDB-backed) path — see the async-boundary finding above. Yes, structurally, for
    `InMemoryChunkStorage`, but that mode is explicitly dev/test-only.
27. **Is async generation justified now?** No — the existing storage-layer async boundary already
    provides the decoupling a dedicated job system would add, with no measured GameLoop stall to
    justify the additional complexity.
28. **Any new abstraction justified by repeated real pressure?** One: `WorldIdentity`, and only after
    the first inline attempt was judged not good enough — kept deliberately small (a single static
    method), not a subsystem.
29. **Which proposed abstractions were deliberately rejected?** All the framework-shaped ones listed
    in the brief's Part 46 (`IWorldFeature`, `IFeatureStage`, `IBiomeRule`, `ISurfaceRule`,
    `NoiseRouter`, `FeatureRegistry`, `ConfiguredFeature`/`PlacedFeature`, a `BiomeDefinition` DSL, a
    job scheduler) — no call-site pressure found for any of them.
30. **Does the world feel coherent in a real client?** Not independently confirmed this pass (no
    client available) — the strongest claim this document can honestly make is "internally proven
    consistent and free of the specific defect classes audited," not "confirmed pleasant to explore."
31. **What remains intentionally simplified relative to vanilla?** Hard Y0 deepslate cutoff (not a
    gradient), two-material biome surface model, single fixed ruin shape, no vegetation beyond trees,
    no aquifers, no mob-spawning integration, no lighting-engine changes, coarse five-biome catalog.
    All listed in `docs/world-generation.md`'s "Known simplifications" section.
32. **Highest-value gameplay gap after WorldGen?** Real-client exploration validation is the most
    urgent *verification* gap (nothing here has been seen in a real client yet). For *feature* work,
    the brief's own suggested next phase — Survival World Integration (resource progression, tool
    tiers, hunger/saturation, mob spawning/ecology) — is the natural next step now that the world
    itself has a credible, tested foundation to build that on.

# ADR §72 — FastNoiseLite leaf + anti-pillar height (composition)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §72`.

**Choice:** Vendor Auburn **FastNoiseLite** (single MIT `FastNoiseLite.cs` under `World/Noise/` — no official NuGet) as the overworld noise leaf, and fix height **composition** that produced 1×1 pillars.

1. **Leaf** — OpenSimplex2 + FBm via FastNoiseLite; Zenith configures seed/frequency/octaves only (`OverworldNoiseFields`). Delete homemade `SimplexNoise`.
2. **No per-block hash on height** — remove `Hash(x,z)%N` jitter from surface Y.
3. **Continuous climate bias** — `ContinuousHeightBias(temp, rain)` with smoothstep weights (hills/desert/ocean) so discrete biome flips do not cliff ±10–15.
4. **Gentler hills** — height frequency `1/128`, 3 octaves, amplitude 22; continuity contract `MaxAdjacentSurfaceStep = 6` (enforced by leaf test).
5. No hard ocean `Min(sea-1)` — ocean depth comes from continuous bias only (hard clamp was the cliff source).

**Supersedes:** §71 homemade Simplex leaf + discrete/hash height bias. Keeps Dimension seam and five biome kinds.

**Why:** Playtest showed pleasant macros with local “random columns.” Root cause was composition (hash jitter + hard biome bias), not missing a NuGet. FastNoiseLite is the maintained portable leaf; ADR records “do not invent another Simplex.”

**Non-goals:** NuGet wrapper packages (VL.* etc.); domain warp / 3D density terrain; Nether/End; rewriting stored `c:` columns; PM 3×3 populate.

**Status (jul 2026):** Shipped — vendored FNL + continuous bias + continuity test.

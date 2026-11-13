# ADR §65 — Worm cave carvers (H1#8 — caves v2)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §65`.

**Choice:** Replace §64 hash “Swiss cheese” with **deterministic worm segments** + rare deep **cheese spheres**.

1. **`OverworldCaveCarver`** — per chunk 2–4 worms (48–128 steps, turning path); radius by depth (1 shallow → 3 deep); ~1/9 chunks get a deep cheese sphere.
2. **`OverworldCaveContext`** — precomputes segments for 3×3 chunk neighborhood once per column build (perf); point `SampleBaseBlock` uses live query (same geometry).
3. **Guards:** no carve at `y >= surface - 4` or bedrock floor; features after fill+carve.
4. **Flat** unchanged.

**Why:** Playtest + design review — hash caves were not explorable; worms match vanilla *shape* without BDS carver tables or noise libs.

**Non-goals:** aquifers, lush/dripstone biomes, carver types (ravine, nether), persisting `c:` on miss.

**Status (jul 2026):** Shipped — worm carver + context + leaf tests.

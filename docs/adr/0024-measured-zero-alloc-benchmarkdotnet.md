# ADR §24 — Measured zero-alloc (BenchmarkDotNet)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §24`.

**Choice:** Project `src/zenith.Benchmarks` measuring `BinaryStream`, a small `GamePacket` encode path, and `EventBus.Publish`. Package version in `Directory.Packages.props`. Numbers live only in `docs/dx.md` (“Measured”). Optional manual alloc probe — not a CI gate for alpha.

**Adendo (jul 2026 — worldgen suite):** `WorldgenColumnBenchmarks` (flat vs noise column, cave context, LevelChunk encode) + `WorldgenPreSpawnBenchmarks` (`GetRadiusAsync` radius 2 / 4). Filter: `-f *Worldgen*`. Join budget signal for ADR §69 — still optional ShortRun, not CI. Cave alloc leaf → §75.

**Why:** Wire zero-alloc work lacked numbers; avoids claiming DX without evidence.

**Deferred:** Full-server load harness as required CI.

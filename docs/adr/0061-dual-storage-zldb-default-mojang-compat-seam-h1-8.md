# ADR §61 — Dual storage — ZLDB default + Mojang-compat seam (H1#8 direction)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §61`.

**Choice:** Keep **two layers** separate forever:

1. **Engine / on-disk format** — Zenith product worlds stay on **`Zenith.LevelDB` (ZLDB)** (ADR §11: RAM SortedDictionary + WAL + one snapshot). Do **not** replace ZLDB with a full Mojang/Vedrock LSM on the LAN hot path.
2. **World key schema** — Zenith keys (`c:` / `ov:` / `ct:` / `inv:` / `pd:`) vs Mojang BDS keys (`subchunk` / `player_*` / …) are different products. Never mix both schemas in one DB “and hope.”

Gameplay / `World` talk only to **`IChunkStorage`**. Backends:

| Backend | Role |
|---------|------|
| `InMemoryChunkStorage` | tests |
| `LevelDbChunkStorage` → ZLDB | **default** ops path |
| Future `MojangChunkStorage` (name TBD) | optional: open a BDS world folder via a **Mojang-shaped** LevelDB leaf (zlib LSM — e.g. binding/`df-mc/goleveldb` lineage, Vedrock `vlang/leveldb` as study ref). Not a rewrite of `libs/leveldb`. |

**Conversion (second product):** an **offline** CLI / one-shot tool BDS → Zenith world (ZLDB + Zenith keys). Runs outside GameLoop. Result is a native Zenith world, not a hybrid. Live write-back to Mojang folders is Deferred until import read-path is honest.

**Ops honesty (§20):** `zenith.yml` must make the mode explicit (e.g. storage kind / path semantics). Never silently reinterpret a ZLDB dir as BDS or the reverse. Warn loudly on mismatch.

**Why:** Users who want Mojang fidelity should plug a real Mojang-compat LevelDB behind the seam — not force Zenith’s perf/DX store to become RocksDB-theatre. Vedrock’s LevelDB (zlib, goleveldb-derived) is a **reference** for engine A; Dragonfly/PNX/PM show softcore `players/` splits we deliberately avoid for native data (§60). Dual-path + converter beats “one engine to rule them all.”

**Spike order (when implementing):** (1) freeze what `IChunkStorage` must expose for a read-mostly Mojang backend, (2) leaf spike: open one BDS folder, read spawn column / one key family, (3) offline converter MVP → reopen under ZLDB, (4) only then live Mojang write-path.

**Refs (not in-repo):** `~/Development/references/bedrock/Vedrock`, `vlang-leveldb`, `goleveldb-mcpe` (`df-mc/goleveldb`).

**Non-goals this ADR:** shipping the Mojang backend or converter in the same PR as the decision; noise/biome gen (separate H1#8 product choice); replacing ZLDB; silent path migrators; multi-world load framework.

**Status:** Decision recorded (jul 2026). Implementation = future PRs under H1#8.

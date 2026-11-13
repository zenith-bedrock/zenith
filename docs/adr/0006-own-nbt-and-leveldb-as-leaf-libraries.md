# ADR §6 — Own NBT and LevelDB as leaf libraries

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §6`.

**Choice:** `Zenith.Nbt` (LE / Network / BigEndian) and managed `Zenith.LevelDB` — no NuGet native LevelDB, no Mojang world format decoder.

**Why:**

- Palette dumps are gzip + **BigEndian**; network property data is **Network** NBT — one wrong endian and terrain is air.
- Zenith keys (`c:`, `ov:`) only need a trustworthy KV, not vanilla world decode.
- Managed LevelDB removes native binding pain on Windows/.NET and keeps the dependency graph clean (same “leaf” idea as Nbt).

Trade-off: existing NuGet LevelDB dirs are not migrated — recreate world paths.

**Adendo (jul 2026 — dual storage / Mojang):** ZLDB remains the owned product store (§11). Opening or converting Mojang/BDS worlds is a **separate** backend + offline converter behind `IChunkStorage` — ADR §61. Do not decode vanilla worlds inside `Zenith.LevelDB`.

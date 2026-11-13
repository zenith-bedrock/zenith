# ADR §39 — Inventory + chest LevelDB persist (`ct:` / `inv:`)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §39`.

**Choice:** Same world LevelDB as `c:`/`ov:`. Keys `ct:x:y:z` and `inv:{uuid:D}` with packed slot blobs. Puts remain fire-and-forget on the tick/network path; LevelDB tracks in-flight chest/inv tasks and `FlushAsync` awaits them on graceful shutdown (§41). World hydrates chests at boot; login `TryLoadInventory` before first content; quit enqueues Put. InMemory storage keeps ct/inv dicts for tests. No `players/` volume (§20).

**Adendo (jul 2026 — SlotBlob v2 / §55):** On-disk format is **`SlotBlob` version=2** (`u8 kind` + `i32 value` + `i32 count` per slot). Version=1 (`i32 value` + `i32 count`, value assumed Block) still **loads via migrate-on-read**: `Tools.IsTool(value)` → `StackKind.Item`, else `StackKind.Block`. New Puts always write v2. Ops: existing LAN worlds with v1 `inv:`/`ct:` upgrade transparently on read; no offline rewrite tool required.

**Why:** Process restart was wiping bags/chests — ops honesty without Mojang playerdata.

**Deferred:** Ender chest, armor. **Position + GameMode → §60**. **Double-chest → §56** (persist still 2×`ct:`). **`players/` volume** still Deferred (§20 / §60).

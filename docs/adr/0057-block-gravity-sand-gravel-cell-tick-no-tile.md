# ADR §57 — Block gravity — sand/gravel cell tick (no Tile / FallingBlock framework)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §57`.

**Choice:**

1. **Domain:** curated gravity set only — `minecraft:sand` + `minecraft:gravel` (add gravel to `Blocks` placeable / DigProfiles / Creative short list with sand). No concrete powder, anvil, dragon egg, scaffolding, or snow in this leaf.
2. **Support rule:** a gravity block is unsupported when the cell immediately below (`y-1`) is air **or** outside world vertical bounds (same FlatMinY floor as place). Any non-air below = supported (chests, overlays, flat base stone/dirt/grass all count — no full collision/shape table).
3. **Tick model (no Tile):** `GravitySystem` on GameLoop, after `BlockSystem` in registration order. World holds a sparse **pending fall** set of cell keys (reuse FloorDropStore SoftCap pattern — warn + refuse new keys past cap; merges/refreshes of existing keys OK). **Not** a BlockActor graph, **not** `Capabilities/`, **not** WorldEntity/ECS.
4. **Triggers (decide on tick, never on RakNet thread):**
   - After a successful place/break mutation that may unsupport neighbors: enqueue the placed cell if gravity; enqueue the cell **above** a broken/replaced cell if that above cell is gravity.
   - Optional same-tick flood: while draining, if a fall lands and the new above cell is gravity, enqueue it (column cascade) — bounded per tick (e.g. max N cell steps globally) so one dig under a sand tower cannot stall the loop.
5. **Fall step:** for each pending cell, if still gravity + unsupported: `SetBlock` source → air, `SetBlock` landing → rid (scan down to first supported cell + 1). Fan-out both cells via existing `UpdateBlock` peer path (`BlockSystem` helpers / `WorldProtocol.PublishUpdateBlocks`). **No** `AddActor` falling_block entity wire in MVP — client sees discrete cell moves; LAN honesty > vanilla fall animation.
6. **Landing / crush:** land on first non-air below; if scan hits FlatMinY with only air, destroy the falling block (no void entity). **No** player/entity crush damage; **No** item drop on “break mid-air” (block either lands or vanishes at void).
7. **Persistence:** falls are RAM pending only. On graceful stop, either drain all pending to settled overlays before flush **or** document “in-flight falls lost on hard kill” (same class as unflushed Puts). Settled cells remain normal `ov:` overlays — no gravity-specific LevelDB keys.
8. **Cross-layer:** Gameplay decides + mutates World; Protocol only UpdateBlock. Packets stay free of World/Player. Do **not** put gravity flags on DigProfiles — separate sparse map or `Blocks.IsGravity(rid)` allowlist (§55 heavy domains stay off DigProfile columns).

**Why:** Static sand/gravel is a visible LAN honesty gap after place/break. Cell-tick + UpdateBlock reuses overlay mutation and avoids freezing Tile/FallingBlock/Actor frameworks (Rule 7 / §32).

**Non-goals:** falling_block AddActor animation; anvil/concrete powder; fluid interaction; redstone dust pop; player crushing; mid-air break into FloorDrop; Mojang random-tick; gravity under chests opening; soft-block “partial support” shapes.

**Spike order:** (1) `Blocks.IsGravity` + gravel placeable/dig/creative, (2) pending set + `GravitySystem` drain after BlockSystem, (3) place/break enqueue hooks, (4) leaf tests (tower, dig under, place over air, SoftCap), (5) human smoke.

**Status (jul 2026):** Shipped on develop after `v0.0.2-alpha` — `GravityPendingStore` + `GravitySystem`; gravel in placeables / DigProfiles / CreativeCatalog (net id 20); graceful shutdown settles pending before flush.

**Adendo (jul 2026 — fall rate):** Playtest: `MaxStepsPerTick = 64` collapsed whole towers in one tick (~instant). Dragonfly uses `falling_block` entities (gravity 0.04/tick). Zenith now **`MaxStepsPerTick = 2`** + **deferred enqueue** for cascade cells (processed next tick, not same-tick chain). Shutdown `SettleAllPending` drains immediately without defer.

**Roadmap:** H1#6. Playerdata reconnect → §60 (H1#7). Does **not** unlock world-gen (H1#8).

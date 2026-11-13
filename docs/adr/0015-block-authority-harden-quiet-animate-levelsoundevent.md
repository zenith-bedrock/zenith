# ADR §15 — Block authority harden + quiet Animate / LevelSoundEvent

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §15`.

**Choice:** Keep Handler → FIFO `BlockEditIntent` → `BlockSystem` for place/break. On tick: re-check bounds, Euclidean reach from **eyes** (`PositionY + PlayerEyeHeight`) with `Player.MaxBlockReach = 6`, place only into air (consume hotbar **after** that check), break only non-air and only if `TryAdd` succeeds (never void items). In-game ACK Animate (`0x2c`) and LevelSoundEvent (`0x7b`) by packet id without DTO fan-out. `MobEquipment` Decode updates `SelectedHotbarSlot` only (place still uses baked intent slot).

**Why:** Closes Fase 3 gaps (occupied place / full-hotbar break / unreachable edits) without Actor or domain EventHandlers. Log spam from swing/sound was WARNING noise, not missing gameplay. Reach is intentionally simple (not AABB/physics).

**Deferred:** AuthInput block path, sound peer fan-out, food/effects. (Inventory storage closed in §16. Animate peer fan-out → §53.)

**Adendo (jul 2026 — chat FIFO §54):** `SubmitChat` is a capped FIFO (`MaxPendingChat = 8`, like block edits) — overflow rejects the newest. `ChatSystem` drains all pending messages per player per tick (not overwrite-latest).

**Adendo (jul 2026 — break path):** Survival destroy arrives via `PlayerAuthInputPacket.BlockActions` when `ServerAuthoritativeBlockBreaking=true` in StartGame. Decode past the position prefix (bitset + flags → `PlayerBlockAction`); map `predict_destroy` (26) → `TrySubmitBreak` / `BlockEditIntent` air. Keep `PlayerAction` (13/26) and `InventoryTransaction` UseDestroy as fallbacks. Progress actions (`start_break` / `crack_break` / `continue_destroy`) ignored until §27.

**Adendo (jul 2026 — place vs player AABB):** After air + placeable checks, before `TryConsumeOne`, reject place when the full-cell block AABB intersects the placer or any other InGame player's standing AABB (inset `1e-4` to avoid flush-face false positives). Reuses `Geometry/Aabb` + `EntityHitboxes` (no Entity/ECS). Reject → `ResyncCellToBreaker` only (no neighbor fan-out — avoids self-place sync glitches). Floor drops are not colliders. Creative uses the same rule. **Known debt:** fixed standing BB (no sneak/swim); full-cube cells only until slab models; jump-place while BB still overlaps stays reject (no separate physics). **Smoke:** place into body → reject / no item loss / no stuck; flush adjacent → OK; A in cell + B places → B rejected.

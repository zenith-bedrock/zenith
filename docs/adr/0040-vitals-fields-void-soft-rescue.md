# ADR §40 — Vitals fields + void soft-rescue

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §40`.

**Choice:** `Player.Health`/`Hunger` (20/20) drive `UpdateAttributes` at spawn. Void: if `Y < FlatMinY - 8`, soft-rescue to **world spawn** `(0, FlatSpawnY, 0)` — **no** Health=0 / Respawn handshake (softlock risk). Same-XZ Y-only rescue left players inside dig shafts. Check lives in `MovementSystem` (Rule 7 — no decorative VitalsSystem). Self camera correction uses `MovePlayer` Teleport (§41); `MoveActorAbsolute` alone is insufficient for the local client under client-authoritative movement. Peers still get Absolute via dirty pose fan-out.

**Why:** Attributes literals lied about domain; falling forever was worse than thin vitals. Death wire deferred until respawn protocol is intentional.

**Deferred:** hunger tick, food, fall damage, drowning, death/Respawn packets.

**Adendo (jul 2026 — death / Respawn MVP):** Soft-rescue **superseded** once Respawn wire shipped. Void `Y < FlatMinY - 8` now sets `Health = 0`, `IsDead`, sends `DeathInfo` (`0xbd`) + `Respawn` SEARCHING (`0x2d`); client `Respawn` CLIENT_READY or `PlayerAction` RESPAWN → `SubmitRespawn` → GameLoop restores Health=20, pose to world spawn `(0, FlatSpawnY, 0)`, Teleport + attributes + Respawn READY, then **InventoryContent + UiInventoryContent** (client clears bag UI on death). **Death loot → §73** (Survival dumps to floor; Creative keepInventory). AuthInput / block edits ignored while dead. No VitalsSystem / DamageSystem; logic stays in `MovementSystem` + handler intents. Hunger/food/fall/drowning still Deferred.

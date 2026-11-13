# ADR §46 — Chest facing only (no double-chest)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §46`.

**Choice:** Palette secondary index `name + one string-state → network_id` (chest `minecraft:cardinal_direction`). Live place remaps chest item rid → facing from player yaw in `InGameSessionHandler` (front toward player = opposite look). `Blocks.Chest` remains south (item/recipe/creative). `IsChest` covers all four rids; break drops canonical `Blocks.Chest`. `BlockSystem` does not reorient intents submitted as raw south (tests / tools).

**Why:** Adjacent south-south chests client-merge as a visual double while server keeps 2×27 (`ct:`). Correct facing is medium polish without BlockActor / pair model.

**Deferred:** Trapped/ender/copper; stairs/beds/doors facing stack. **Double-chest → §56.**

**Known (historical):** Two adjacent chests with the same cardinal still meshed as a double on the Bedrock client while server kept 2×27 — fixed by §56 pair open.

**Smoke:** Place chest while looking each cardinal → UpdateBlock rid matches; break always returns stackable south item; open/break works on any facing.

**Adendo (jul 2026 — PlaceFacing):** Yaw→cardinal (+ opposite / front-toward-player) lives in `PlaceFacing`; `ChestFacing` is a thin chest place wrapper. Next directional block reuses the helper and stays allowlisted in `Blocks` — still no furnace/stairs product stack, BlockBehavior framework, or double-chest (roadmap / Deferred above). Lid wire is §28 adendo.

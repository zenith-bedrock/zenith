# ADR §38 — CraftCreative from CreativeCatalog SSOT

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §38`.

**Choice (updated Jul 2026):** `CreativeCatalog` on `ServerContext` (net ids 1–7). Protocol `BuildCreativeContent` from catalog. ISR `CraftCreative` is a first-class `InventoryStackAction` (same contract as `CraftRecipe`): materialize `MaxStack` into `InventorySlotReference.CraftResult` (CreatedOutput), then honor same-request Place/Take/Drop/Create. `NumberOfCrafts` on CraftCreative is protocol boilerplate (ignored; do not reject times==0). Recipe+Creative in one request rejected. Creative-only (handler + system). Wire `WireTouch` echoes; response includes CreatedOutput (60) for craft path.

**Why:** Palette vanilla is CreatedOutput→cursor/bag, not bag `TryAdd`. The prior `CraftCreativeNetId` parallel intent with empty Actions discarded client Places (click never reached cursor).

**Deferred:** full `creative_items.json` / `block_state_b64`; ActionDestroy creative trash (Unsupported → whole-request reject today).

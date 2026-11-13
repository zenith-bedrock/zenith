# ADR §29 — Crafting 2×2 — RecipeRegistry MVP (wire completed in §35)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §29`.

**Choice (updated Jul 2026):** Ephemeral `PlayerCraftUi` (grid slots `InventorySlotReference.CraftGrid(0–3)`, result `CraftResult`) outside `PlayerInventory` — chest-flat pattern. ISR maps containers 13/60; `CraftRecipe`+`Create`+`Consume` bake into one intent; tick `TryCraftFromGrid` then materialize result. Window **124** UI content on spawn + inventory open. Bag-only `TryCraft` remains for tools/tests. **`CraftRecipe.NumberOfCrafts` honored** (shift-click output): consume ×N, result `out×N` clamped by grid affordability and single-slot `MaxStack` (H0).

**Why:** Empty ISR OK / bag consume never drove SAI craft UI (S35). Grid + Create is enough without a full 54-slot UI inventory. Discarding times made shift-click Place N×out fail after craft ×1. Skipping CreatedOutput (60) in ItemStackResponse desynced sequential take after planks→chest — **fixed:** always emit container 60 on OK; refuse Survival craft while Result still occupied.

**Deferred:** 3×3 crafting table; recipe-book **CraftRecipeAuto**; container 14 preview sync; negative stack-net-id prediction; multi-stack CreatedOutput when out×N > MaxStack; double-click gather (no dedicated ISR opcode — client multi Place/Take, intermittent).

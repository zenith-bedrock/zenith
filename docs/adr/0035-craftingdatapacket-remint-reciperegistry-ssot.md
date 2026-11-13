# ADR §35 — CraftingDataPacket remint (RecipeRegistry SSOT)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §35`.

**Choice:** `CRAFTING_DATA_PACKET = 0x34` after `CreativeContent`, before empty `BiomeDefinitionList`. Protocol builds shapeless DTOs from `RecipeRegistry.SnapshotRecipes()` + `ItemPalette` + `Blocks.TryGetName` — **no** second `CreateDefault` recipe list in Packets. `ClearRecipes = true` intentionally replaces the vanilla book with Zenith’s two recipes only. Unlock AlwaysUnlocked; UUID zeros; block `"crafting_table"`; `RecipeNetworkID` = registry net id (1 / 2).

**Why:** Without remint, client 2×2 never sends our ISR net ids — registry craft stays dead on the wire. SSOT in Protocol avoids CreativeContent-style recipe table drift.

**Deferred:** Shaped grid, AutoCraft, full Mojang dump, 3×3 table.

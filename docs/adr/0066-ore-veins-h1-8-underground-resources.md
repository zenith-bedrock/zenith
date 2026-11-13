# ADR §66 — Ore veins (H1#8 — underground resources)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §66`.

**Choice:** Deterministic **8×8×8 cell clusters** (Chebyshev radius 2) replacing stone/deepslate hosts only.

1. **`OverworldOrePlacer`** — ~1/7 cells spawn a vein; ore type by Y band (coal→copper→iron→gold→redstone→lapis→diamond); deepslate variants when host is deepslate (Y &lt; 0).
2. **Sampler order:** caves → terrain layers → ore replace → surface features unchanged.
3. **`Blocks` + `DigProfiles`:** overworld + deepslate ore set; pickaxe harvest; break drops ore block item (no fortune/smelting yet).

**Why:** Playable survival mining before biomes; smallest leaf that does not need loot tables or feature JSON.

**Non-goals:** fortune/silk touch loot, ore smelting recipes, nether/emerald/quartz, biome-scaled rates, persisting `c:` on miss.

**Status (jul 2026):** Shipped — ore placer + blocks + leaf tests.

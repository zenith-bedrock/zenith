# ADR §67 — Coarse overworld biomes (H1#8 — biome v1)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §67`.

**Choice:** **48×48** deterministic biome regions — no noise libs, no BiomeDefinitionList payload.

1. **`OverworldBiomeSampler`** — ocean / plains / desert / hills / forest; Bedrock network ids (0–4); height bias + surface block (sand vs grass) + tree density.
2. **Column wire** — `ChunkPayloads` writes sampled biome id per subchunk section (center of chunk).
3. **StartGame** — vanilla spawn settings use `SpawnBiomeType::Default` (0); actual biome network IDs remain in the chunk biome palettes. `UserDefinedBiomeName` is reserved for custom biome definitions, which Zenith does not author.
4. **Flat** unchanged (plains biome + grass column).

**Why:** Visible variety + correct client biome tint/F3 without a definition dump or BDS tables.

**Non-goals:** biome JSON, 3D biome blending, taiga/swamp/jungle, mob spawn rules, **custom** BiomeDefinitionList authoring, persisting `c:` on miss. (Embedded vanilla list for join is §70 — not this product.)

**Status (jul 2026):** Shipped — sampler + column wire + spawn biome + leaf tests.

**Adendo (out 2026 — StartGame spawn enum):** The value written to `SpawnBiomeType` is an enum (`Default=0`, `UserDefined=1`), not a biome network ID. The former `SampleSpawnBiome` path passed vanilla biome IDs (including `plains=1`) as that enum and sent a built-in name as `UserDefinedBiomeName`. Removed that conflation: Zenith now sends `Default` with an empty user-defined name; actual terrain biomes continue to be transmitted in chunk palettes. Added a StartGame wire regression test. No packet was added.

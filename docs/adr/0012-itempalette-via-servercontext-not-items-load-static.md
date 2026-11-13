# ADR §12 — ItemPalette via ServerContext (not Items.Load static)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §12`.

**Choice:** Load full `item_palette.json` into an immutable `ItemPalette` held on `ServerContext`. Protocols map block runtime → wire item id using `Blocks.TryGetName` + palette. Send `ItemRegistryPacket` (0xa2) after StartGame. Packets hold only `NetworkItemStack` DTOs.

**Why:** Clients need the full registry; hardcoded IDs lied about air (`0` vs `-158`). Injecting the palette keeps dependencies visible — **do not** grow another `Blocks.Load`-style static for items/biomes/mobs.

**Known debt:** `Blocks.*` remains a static façade for gameplay consts (`Blocks.Stone`, etc.). Next registry goes through Context; migrate Blocks when inventory/block domain is next touched.

**Fallback:** boot `Require` air/stone/grass (throw if missing); runtime unknown block → air item id + Warning capped at 64 distinct ids (never fake stone).

**Version SSOT:** `ServerIdentity.VersionName` drives StartGame / ResourcePackStack strings. ItemRegistry wire layout is documented against `ServerIdentity.ProtocolVersion`.

**Deferred:** full vanilla `creative_items.json` / `block_state_b64` parse (file remains embedded, intentionally unused — wire CreativeContent comes from `CreativeCatalog` only); behavioral item classes.

**Adendo (jul 2026 — registry maturity):** Dump-driven stance: full palettes for wire fidelity; curated `Blocks` façade for gameplay; creative/recipes stay separate SSOTs. `BlockPalette` records every `network_id → name` at NBT parse (`TryGetName`); `Blocks.TryGetName` uses curated map then palette reverse so Protocol name→ItemPalette bridge does not air-fallback for in-dump states outside the starter set. `Blocks.IsPlaceable` allowlists curated placeables (+ chest facings); handler + `BlockSystem` reject others. Still **not** dual block-id registries, a typed mega-registry, or Blocks→Context (§25).

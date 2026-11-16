# Blocks: palette, catalog, placement, digging

How a block becomes placeable and breakable, and what is deliberately not covered yet. Source of truth is the
code under `src/zenith/World/`; this page explains the model.

## Layers

| Layer | File | Role |
|---|---|---|
| Palette | `BlockPalette.cs`, `BlockState.cs` | All 1477 block types / 22,091 states from `block_palette.nbt`: name ↔ runtime id, each id's full property set, legal values per property, exact and override-based state lookup. |
| Properties | `BlockProperties.cs`, `data/block_properties.json` | Hardness, blast resistance, light, flammability per block name (1321 names). Imported from the PocketMine-derived table in `essential-reference` (LGPL-3.0); obsidian/crying obsidian overridden to 50 per the Bedrock wiki. Not a BDS capture — treat as reviewed-but-unverified. |
| Catalog | `BlockCatalog.cs`, `BlockRules.cs` | Classifies each block by state signature and name into a `BlockKind` and derives a dig profile (tool, tier, drop gating). |
| Placement | `BlockPlacement.cs` | Turns "held block + face + cursor height + yaw" into the runtime id actually placed, and checks ground support for plants. |
| Drops | `BlockLoot.cs` | Ore drops, redirects (stone → cobblestone, grass → dirt, double slab → 2 slabs) and "drops nothing" (glass, leaves, ice). |

Curated entries (`DigProfiles`, `SolidBuildingBlocks`, the original placeable list) still win over derived ones.

## Kinds

| Kind | Placement decides | Examples |
|---|---|---|
| Cube | nothing — placed as held | stone, glass, panes, planks, wool, ores, leaves, fences |
| Pillar | `pillar_axis` from the clicked face | logs, wood, basalt, chains |
| Stairs | `weirdo_direction` from yaw, `upside_down_bit` from face / cursor height | every `*_stairs` |
| Slab | `minecraft:vertical_half` from face / cursor height | every `*_slab` |
| DoubleSlab | never placed from an item | `*_double_slab` (drops two slabs) |
| Plant | ground support (soil for flowers/saplings/grass, any solid for mushrooms/fungi/roots); passable | flowers, saplings, short grass, ferns |
| Torch | `torch_facing_direction` from the clicked face; passable | torch, soul torch |
| Unsupported | — | doors, trapdoors, containers, liquids, fire, walls, signs, redstone, beds |

Unsupported blocks are neither placeable nor breakable by the derived rules, so an unsimulated block cannot be
half-working.

## Known gaps

- Slabs do not merge into double slabs yet (placing on the matching half places a second slab in the neighbour cell).
- Stair corner shapes (`minecraft:corner`) stay `none`; the client does not infer them.
- Walls, doors, trapdoors, fence gates, beds and signs need multi-block or interaction logic.
- Tool classification is a name heuristic (`BlockToolRules`) with exact vanilla values for the common families;
  hoe/shears/sword-effective blocks (leaves, hay, cobweb) are treated as "any tool".
- Plants only break when the block directly under them is removed; no general neighbour-update propagation exists yet.
- Hardness data comes from an unverified third-party table (see above).

## Validation

- Unit: `BlockStateTests`, `BlockCatalogTests` (classification, derived tools/tiers, placement states, drops).
- Wire: `zenith-smoke-bot` `bun run smoke:blocks` places stairs/slab/log/plants from the creative catalogue against a
  running server and asserts the exact runtime ids from `block_palette.nbt`. Not yet verified in a rendered client.

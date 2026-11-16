# Blocks: palette, states, catalog, shapes, digging

How a block becomes placeable and breakable, and what is deliberately not covered yet. Source of truth is the
code under `src/zenith/World/`; this page explains the model.

## Layers

| Layer | File | Role |
|---|---|---|
| Palette | `BlockPalette.cs`, `BlockState.cs` | All 1477 block types / 22,091 states from `block_palette.nbt`: name ↔ runtime id and each id's property set as strings. The string API is for loading and diagnostics; runtime code uses the typed layer below. |
| States | `States/` | `StateKeys` is the single place that names a property and spells its values (`open_bit`, `minecraft:cardinal_direction`…); `BlockFamily` lays each block type out as a mixed-radix table, so `BlockStateTable.TrySet(ref id, StateKeys.Open, true)` finds the new runtime id by arithmetic — typed, 0 B allocated, ~13 ns. `Cardinal`/`Face` replace direction strings. |
| Properties | `BlockProperties.cs`, `data/block_properties.json` | Hardness, blast resistance, light, flammability per block name (1321 names). Imported from the PocketMine-derived table in `essential-reference` (LGPL-3.0); obsidian/crying obsidian overridden to 50 per the Bedrock wiki. Not a BDS capture — treat as reviewed-but-unverified. |
| Catalog | `BlockCatalog.cs`, `BlockRules.cs` | At build time classifies every family into a `BlockKind`, derives its dig profile and `BlockTraits` (soil, needs-soil, redstone-only, wall-connectable). Runtime queries are lock-free lookups. Name heuristics live only here. |
| Shapes | `Shapes/`, `BlockShapes.cs` | One `BlockShape` per kind owns its rules: placement state, `CanExist` (support), `HasRoom`, `UpdateShape` (neighbour-dependent state), `TryInteract`, door halves. `BlockShapes` routes an id to its shape; shapes read the world and report `BlockWrite`s, the gameplay tick commits them. |
| Updates | `BlockUpdateQueue.cs`, `ScheduledTicks.cs`, `BlockUpdateSystem.cs` | World writes queue their cell; the tick asks each neighbour `CanExist` (break + drop if not) and `UpdateShape` (rewrite if it changed). ADR §164. |
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
| Door | `cardinal_direction` from yaw, upper half written with it, needs ground and headroom; right click toggles both halves; breaking either half removes both; drops the door item | wooden/copper doors (iron: redstone only) |
| Trapdoor | `direction` + `upside_down_bit` from yaw / face / cursor; right click toggles | every `*_trapdoor` |
| FenceGate | `cardinal_direction` from yaw; right click toggles | every `*_fence_gate` |
| Wall | connections and post computed from neighbours (walls, gates, opaque cubes; tall under a block) by `UpdateShape`, so any neighbour change reshapes it | every `*_wall` |
| Unsupported | — | containers other than chest/crafting table, liquids, fire, signs, redstone, beds, furnaces |

Slabs merge: a second slab of the same type on the open half (clicked slab, or target cell) becomes the matching
`*_double_slab`. Open doors, trapdoors and gates do not block movement.

Unsupported blocks are neither placeable nor breakable by the derived rules, so an unsimulated block cannot be
half-working.

## Known gaps

- Stair corner shapes (`minecraft:corner`) stay `none`; deliberately skipped until the left/right semantics are
  verified against a real client.
- Furnaces need a block entity, smelting and a window; liquids, fire and redstone need the block-tick foundation first
  (`plans/world-dynamics-2026-11-15.md`). Beds and signs need block entities.
- Door/trapdoor/gate orientation and wall shapes follow the PocketMine state encoding; none of it has been checked in a
  rendered client yet (only exact runtime ids on the wire).
- Tool classification is a name heuristic (`BlockToolRules`) with exact vanilla values for the common families;
  hoe/shears/sword-effective blocks (leaves, hay, cobweb) are treated as "any tool".
- Support reactions exist (plants, torches, doors break when what they hang on goes — `BlockUpdateSystem`, ADR §164); gravity, liquids, fire, growth and random ticks are still separate or missing.
- Hardness data comes from an unverified third-party table (see above).

## Validation

- Unit: `BlockStateTests`, `BlockStateTableTests` (typed reads agree with the palette for every state, edit round trips), `BlockCatalogTests` (classification, derived tools/tiers, placement states, drops), `BlockUpdateTests`.
- Wire: `zenith-smoke-bot` `bun run smoke:blocks` places stairs/slab/log/plants from the creative catalogue against a
  running server and asserts the exact runtime ids from `block_palette.nbt`; `BLOCKS_PHASE=2` covers doors and walls. Not yet verified in a
  rendered client.
- Unit: `BlockShapesTests` (doors, toggles, walls, slab merge).

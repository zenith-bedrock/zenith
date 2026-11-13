# ADR §138 — Recipe/item catalog expansion, scoped to what already has real gameplay behind it

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §138`.

**Context:** requested expansion of crafting and the item catalog. `RecipeRegistry`'s own existing
doc comment already states the governing principle from Phase XXV: "recipes here only use materials
a player can actually obtain today, not simulate crafting options currently sitting on the far side
of an unbuilt feature" — iron/gold tools were deliberately left out because no furnace exists to
produce ingots. This pass extends that same principle rather than relaxing it: four new recipes
(torch, oak fence, stone bricks, cobblestone wall), all buildable from materials already minable
today (coal, sticks, planks, stone, cobblestone), no smelting required.

**Choice:** `RecipeRegistry` gains `CoalAndStickToTorch` (1 coal + 1 stick → 4 torch), `PlanksAndStickToFence`
(4 planks + 2 stick → 3 fence), `StoneToStoneBricks` (4 stone → 4 stone bricks), `CobblestoneToWall`
(6 cobblestone → 6 wall) — real vanilla ratios, verified against the live wiki. `CreativeCatalog`
gains matching entries so the same four blocks are reachable in Creative too. `Blocks`/`DigProfiles`
gain the four new block ids, following the exact registration shape ADR §137 established: torch
joins `IsPassableDecoration` (non-solid, like short grass/flowers); fence/stone bricks/cobblestone
wall are ordinary full-solid blocks (Zenith doesn't model partial collision boxes at all — Phase
XXIX — so a fence blocks movement fully rather than at vanilla's partial height, the same accepted
simplification every other thin block already lives with). All four get `DigProfiles` entries
(missing = undiggable) and join `_placeableIds`.

**What was deliberately NOT added, and why each is a separate decision, not an oversight:**

- **Swords.** `ToolKind` has no `Sword` value, and melee damage is a flat `const float AttackDamage`
  hardcoded independently in every one of the 12 species' own combat call sites (not a single shared
  lookup) — unlike pickaxe/axe/shovel, which plug straight into the *already-existing*
  `Tools`/`DigProfiles`/`BreakDuration` dig-speed framework, a sword with no damage differentiation
  would be a purely cosmetic reskin, and wiring real per-weapon damage means touching combat balance
  across every species plus rebalancing away from today's flat baseline (many existing tests hardcode
  post-hit HP values against that flat constant). A real weapon-damage system is a separate,
  legitimately larger feature, not a same-pass add-on to a recipe/item catalog pass.
- **Ladder.** The palette block exists and could be registered as passable exactly like torch, but
  Zenith has no vertical-climb input handling at all — a ladder you can see and place but can't climb
  is a more actively misleading affordance than a torch that doesn't emit light (lighting is *already*
  a documented, low-expectation gap for every block; a ladder visually promises climbability
  specifically). Skipped until a climb mechanic exists to back it.
- **Crafting table, furnace.** `PlayerCraftUi.GridSize = 4` (2×2, matching the player's own inventory
  grid) — there is no separate 3×3 container UI, and Zenith's matcher is aggregate-count shapeless
  (not shape-based), so a "crafting table" block today would open the exact same 2×2 grid the player
  already has, functionally inert. A furnace is worse: placeable-but-non-cooking would be a block that
  actively lies about what it does. Both need real, separately-scoped features (a 3×3 container + UI
  packet flow; a fuel/burn-time tick system) before they can exist without being half-finished.
- **Everything gated on iron/gold/redstone/wheat/wool/leather and similar** (buckets, shears, flint
  and steel, bread, beds, wool-based blocks, dyes) — same "no furnace/no farming/no sheep" gate
  Phase XXV already established for iron/gold tools; this pass didn't touch any of those upstream
  gates, so nothing downstream of them changed either.

**Non-goals:** no `ToolKind.Sword`/weapon-damage system; no ladder/climb mechanic; no 3×3 crafting UI;
no furnace/smelting/fuel system; no recipes gated on smelted or farmed materials.

**Status (ago 2026):** Shipped — `RecipeRegistry.cs`, `CreativeCatalog.cs`, `Blocks.cs`, `DigProfiles.cs`.

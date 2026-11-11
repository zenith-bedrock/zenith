# Inventory / container audit (living document)

Status: **started 2026-11-11.** Zenith's container and inventory handling has known bugs; a passing unit test or smoke
is not evidence of correctness here. Every entry below says how it was established. Nothing in this file is a claim
that an area *works* — only that a specific thing was or was not checked.

Behaviour reference: the Cinnabar client (`cinnabar-reference`, PolyForm Noncommercial — **read for behaviour only,
never copy code or data**) plus Mojang's `bedrock-protocol-docs` for wire shape. Cinnabar's own `inventory` crate is
the closest thing to "what a real client sends and expects back".

## What the real client sends (from Cinnabar's tests and request builders)

- **Personal 2×2 craft:** `CraftRecipe(recipe_net_id, crafts)` → `CraftResultsDeprecated(results, crafts)` →
  one `Consume` per grid slot touched (container `crafting_input`, slots 28..=40) → `Take` from the created output
  (container 60, slot 50, **stack_network_id = the request id**) to the cursor.
- **Recipe book (auto craft):** a dedicated `CraftRecipeAuto` action; ingredients are drawn from the crafting grid
  *and then the player inventory, hotbar first*; the result is placed into the inventory, hotbar first.
- **Workstations (anvil, grindstone, …)** use `CraftRecipeOptional` / `CraftGrindstone` / `CraftLoom`.

## Verified (read against the code, constants checked against Mojang's `ContainerEnumName`)

| Item | Result |
|---|---|
| Container ids 6/7/12/13/28/29/59/60 in `InventoryContainerMap` | match the schema |
| Crafting input slots 28..=40, created-output slot 50 | match Cinnabar's reference ranges |
| ISR failure path | snapshots inventory/armor/craft grids/chest before applying and restores them on failure (read, **not** fuzzed) |

## Known gaps (from reading; **not yet reproduced with a failing test**)

1. **`CraftRecipeAuto` is decoded then always rejected** (`ItemStackRequestPacket`: returns `Unsupported`). The recipe
   book therefore cannot craft. Needs ingredient allocation over grid + inventory shared between the "can craft?"
   check and the actual request, as Cinnabar does.
2. **`Consume` and `CraftResultsDeprecated` are mapped to no-ops.** Consumption is recomputed server-side from the grid,
   so a client whose `Consume` list disagrees with the server's view is never detected. Likely benign for vanilla
   clients; worth a test with a mismatching request.
3. **No offhand container (34).** `InventoryContainerMap.TryMap` has no entry, so any request touching the offhand is
   rejected (and `Hand != Mainhand` use is dropped).
4. **Only chest / personal / crafting-table windows exist.** Furnace, anvil, grindstone, loom, barrel, shulker,
   brewing, trading … are all unmapped (every request touching them is rejected, which is at least safe).
5. **`MineBlock` echoes the held stack without mutating** (no durability).
6. **`Transfer`/`Drop` with `count == 0` means "whole stack"** in `InventorySystem.Apply`; the wire never sends 0 for a
   real move, so this is an input-validation question, not a feature.

## Fixed

- **Silent craft clamp (2b).** `RecipeRegistry.TryCraftFromGrid` used to reduce an over-large `crafts` to what the grid
  afforded; it now refuses. Reproduced first (`CraftingAuditTests`), old characterization test inverted
  (`InventorySystem_craft_times_beyond_grid_is_rejected`). Not validated against a retail client.
- **Open, found while reading:** `RecipeRegistry` holds a tiny hand-written recipe set and matches shapeless by aggregate
  count, so e.g. the chest (ring of 8 planks) is accepted for any arrangement of 8 planks on the table.

- **Recipes (ADR §146).** Hand-written shapeless table replaced by the real vanilla set matched by shape/name/tag; the chest-from-any-8-planks, side-by-side-stick and extra-items-ignored bugs are gone (tests in `RecipeRegistryTests`).

## Next

- Turn each gap above into a failing test *first* (see `InGameInventoryHandlerItemStackRequestTests`,
  `InventoryDuplicationRegressionTests`, `InventoryRearrangeTests` for the harness), then fix.
- Reproduce with the real client: run Cinnabar against Zenith and capture the ISR/response traffic (its
  `tools/bedrock-replay` and `docs/handoffs` describe capture flows) — this is where actual bugs will show up, not in
  reading code.
- Add item-conservation property tests across every store (player, cursor, craft grids, created output, chest) for
  randomised ISR sequences, per the single-writer/conservation rule in `AGENTS.md`.

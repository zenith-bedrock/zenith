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

1. ~~`CraftRecipeAuto` always rejected~~ — **fixed, see below.**
2. **`Consume` and `CraftResultsDeprecated` are mapped to no-ops.** Consumption is recomputed server-side (from the
   grid for `CraftRecipe`, from grid + inventory for `CraftRecipeAuto`), so a client whose `Consume` list disagrees
   with the server's view is never detected. Likely benign for vanilla clients; worth a test with a mismatching request.
3. ~~No offhand container (34)~~ — **implemented, see Fixed.**
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

- **CraftingData validated live (bot, schema 1.26.60).** All 3 700+ recipes parse client-side. Found by bisecting: the shield recipe output needs the trailing `blocking_tick` (i64) in its extra blob (the reader keys it on the shield's item id); without it the whole packet mis-parsed. `NetworkItemStack.IsShield`. The inventory/`ItemV4` writers still send an empty extra blob, which would mis-parse for a shield stack (no shield is ever sent there today).

- **Recipe data now comes from the BDS behaviour packs, not a wire dump.** The old capture-derived file carried 1557
  of 3780 recipes that could never match (BDS's `name:aux` strings such as `minecraft:red_wool:0`, unknown names).
  `bedrock-data recipes` rebuilds it from the pack JSON + explicit generators and `--verify` proves 0 aux-free
  grid recipes are missing. `RecipeRegistry` no longer strips aux, skips unknown outputs silently or bakes BDS block
  ids: names resolve against Zenith's palettes and an unknown name fails the load.
- **Recipe-book craft (`CraftRecipeAuto`).** The request is decoded (recipe id + craft count; the client's ingredient
  descriptors are read past, not trusted) and `RecipeRegistry.TryPlanAutoCraft` allocates ingredients from the open grid
  and then the inventory, hotbar first, naming ingredients before tag ingredients. The result lands in CreatedOutput for the
  request's own Place/Take; unaffordable, over-large, table-only-in-the-2×2, unknown-recipe and result-still-occupied
  requests change nothing (`AutoCraftTests`). Verified: unit/intent level only. **Not verified:** a retail client's exact
  request shape (the Consume list is ignored, see gap 2), tag-vs-tag overlap allocation is greedy (no max-flow), and
  `extra` recipe outputs are not produced (only `Outputs[0]`, as for `CraftRecipe`).
- **Item aux values (ADR §147).** `StackId` now carries aux; recipes can require/produce it. Beds (colour + dyeing),
  banners, empty maps and suspicious stew recipes are emitted; the wire oracle now matches 4045 recipes with 0 missing.
  Still open: tipped arrows (need a potion model), firework stars/rockets (colour lives in stack NBT, not modelled),
  durability (`MineBlock` still echoes without mutating).

## Next

- Turn each gap above into a failing test *first* (see `InGameInventoryHandlerItemStackRequestTests`,
  `InventoryDuplicationRegressionTests`, `InventoryRearrangeTests` for the harness), then fix.
- Reproduce with the real client: run Cinnabar against Zenith and capture the ISR/response traffic (its
  `tools/bedrock-replay` and `docs/handoffs` describe capture flows) — this is where actual bugs will show up, not in
  reading code.
- Add item-conservation property tests across every store (player, cursor, craft grids, created output, chest) for
  randomised ISR sequences, per the single-writer/conservation rule in `AGENTS.md`.

- **Offhand (gap 3).** `InventorySlotArea.Offhand` / `PlayerInventory.Offhand`: ISR container 34 slot 1, InventoryContent window 119, persisted under its own storage key (`oh:`), restored on login, resent on spawn/respawn/dimension change, fanned out to peers via MobEquipment window 119 (also on join announce), dropped as death loot, rolled back with the rest of a failed request. Tests in `OffhandTests`. **Not verified with a retail client.** Known limits: no per-item "allowed in offhand" check (the data is not in the BDS packs for legacy items, so any stack is accepted); the client's own MobEquipment window-119 packet is only acknowledged, the ISR move is the single writer; no use/swap (`Hand != Mainhand`) behaviour; the offhand is covered by the death-loot conservation test.

- **Conservation property test.** `InventoryConservationTests`: 5 seeds x 150 random Transfer/Swap requests over bag, cursor, offhand, armor and the personal craft grid; per-kind totals (aux-aware) must never change, whether a request applies or rolls back. Guarded against a vacuous pass (>= 20 requests must actually move something). A second set of 5 seeds runs with a double chest open (54 extra slots, both halves in the census). A third set (survival) adds Drop and Destroy: the player + floor-drop census must stay constant, so a drop lands in full and the Creative-only Destroy deletes nothing in survival. Not covered: the crafting-table window, Creative Destroy, multi-player interleaving.

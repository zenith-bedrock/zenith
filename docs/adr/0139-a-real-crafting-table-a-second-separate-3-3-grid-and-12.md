# ADR §139 — A real crafting table: a second, separate 3×3 grid, and 12 of 17 recipes actually need one

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §139`.

**Context:** user report — chest is craftable without a table, and real vanilla requires one. Auditing
this against real vanilla recipe *shapes* (not just the one reported case) found the same gap in 11
more of ADR §138's own recipes: every tool (pickaxe/axe/shovel, all three material tiers — 3×3 or
2×3 or 1×3 bounding boxes), fence (3×2), and cobblestone wall (3×2) all exceed a 2×2 grid's bounding
box in real vanilla, exactly like chest (3×3 ring) does. Only log→planks, planks→stick, coal+stick→
torch, and stone→stone-bricks genuinely fit 2×2. Zenith's matcher is shapeless (aggregate count, not
arrangement), so nothing about the existing engine would have refused these on its own — the gate had
to be added explicitly, per recipe, from real vanilla's actual shape for each one.

**Wire protocol, verified against two independent reference implementations (Dragonfly and pmmp's
BedrockProtocol, both reverse-engineered from the client's own `PlayerUISlot` enum) before writing
any code:** the personal 2×2 grid (wire slots 28-31) and a crafting table's 3×3 grid (wire slots
32-40) are **two separate, non-overlapping wire ranges on the real client, not one grid that grows**.
Both share protocol container id 13 (`CraftingInput`); which range is meaningful depends entirely on
which window is currently open, matching real client behavior (`Dragonfly`'s `craftingOffset()`/
`craftingSize()` switch on `openedContainerID == ContainerTypeWorkbench`). This directly shaped the
implementation — a naive "just make the existing grid bigger" design would have been wire-incorrect.

**Choice:**
1. **`ICraftGrid`** — a small new interface (`GridSize`, `GetGrid`/`TrySetGrid`, `CaptureSnapshot`/
   `RestoreSnapshot`) capturing exactly what `RecipeRegistry.TryCraftFromGrid` needs, letting one
   method serve both grid sizes. Deliberately excludes the created-output slot: wire slot 50
   (`CreatedOutput`) is shared/unchanged between both grids on the real client, so there is exactly
   one result holder (`PlayerCraftUi.Result`) regardless of which grid produced it.
2. **`PlayerTableCraftUi`** — a new, separate 9-slot class implementing `ICraftGrid`, not a resize of
   `PlayerCraftUi`. `PlayerCraftUi.GridSize` stays a `const 4` (unchanged, zero ripple to its many
   existing static-const call sites); the table grid is a second, independent instance on `Player`
   (`Player.TableCraftUi`), same ephemeral/not-persisted lifecycle as the personal one.
3. **`Recipe.RequiresTable`** — a new field, set per-recipe from each recipe's real vanilla bounding
   box (see above), checked inside `TryCraftFromGrid` itself (`isAtCraftingTable` parameter) — refused
   there even when the aggregate-count match would otherwise succeed, since Zenith's shapeless matcher
   has no other way to know the ingredients don't actually arrange into a shape that small.
4. **`OpenContainerSession.TargetKind.CraftingTable`** — a third target alongside `PlayerInventory`/
   `Chest`. Unlike Chest, it carries no view payload: a crafting table has no shared/persistent
   storage at all, the 3×3 grid belongs entirely to the opening player's own `PlayerTableCraftUi`.
   `InventorySystem.ApplyWindow` gained `InventoryWindowIntent.Kind.OpenCraftingTable`, mirroring
   `OpenChest`'s shape (interact → queue intent → open on tick) including the same re-transmit-crash
   guard `OpenInventory` already has for redundant `ContainerOpen` sends. Close needed no new
   handling — the existing generic `else` branch (`TryCloseContainer`) already covers any non-Chest
   target correctly.
5. **`CraftingDataPacket.Block` field fixed as a side effect of this audit** — it was hardcoded to
   default to `"crafting_table"` for *every* recipe (the field's own C# default), including ones
   craftable in the personal grid. This was a real, pre-existing bug, unrelated to but surfaced by
   this work: `CraftingDataBuilder` now sets `Block = snap.RequiresTable ? "crafting_table" : ""` per
   recipe.
6. **The crafting table itself is a new recipe** (4 planks → 1 table, `RequiresTable: false`) — a
   plain 2×2 block of planks, matching real vanilla, which lets a player bootstrap their first table
   without one.

**Death loot closes the same gap ADR §138 didn't need to think about yet:** `FloorDropFanout`'s
death-loot collector already walked the personal grid; it now also walks `TableCraftUi` before
`Player.CraftUi.Clear()`/`Inventory.Clear()` run, so materials sitting in an open table's grid at
death are conserved (dropped), not silently lost — same "one conserved commit" invariant ADR-era
tests already enforced for the personal grid.

**Non-goals, deliberately not built:** returning grid contents to inventory on window close (neither
grid does this — matches the *existing* personal-grid gap, not a new asymmetry introduced here);
distance-based auto-close when a player walks away from a table (neither reference server checkout
implements this server-side either — real Bedrock clients appear to self-enforce reach and send their
own `ContainerClose`); multiple simultaneous crafting-table sessions sharing state (real vanilla lets
several players use the same physical table with independent grids, which this design already gets
for free — each player's grid lives on their own `Player`, not the block); shaped-recipe matching
(Zenith stays shapeless — `RequiresTable` is evidence-based per recipe, not derived from an actual
shape grammar the engine doesn't have).

**Status (ago 2026):** Shipped — `ICraftGrid.cs`, `PlayerTableCraftUi.cs`, `Player.cs`,
`OpenContainerSession.cs`, `InventoryWindowIntent.cs`, `InventorySlotReference.cs`,
`InventorySlotResolver.cs`, `InventoryContainerMap.cs`, `ContainerOpenPacket.cs`,
`InventoryProtocol.cs`, `RecipeRegistry.cs`, `CreativeCatalog.cs`, `CraftingDataBuilder.cs`,
`Blocks.cs`, `DigProfiles.cs`, `InGameUseItemHandler.cs`, `InventorySystem.cs`,
`FloorDropFanout.cs`.

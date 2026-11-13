# ADR §55 — Block/item foundation — StackId + sparse capability profiles

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §55`.

**Choice:**

1. **Three layers (always):**
   - **A — Wire registry:** full `BlockPalette` / `ItemPalette` dumps; any dump rid may exist in overlay (passthrough).
   - **B — Stack identity:** `StackId(StackKind, Value)` — `Block` = block runtime id; `Item` = item network id. Inventory / chest / craft / floor drops use `StackId`, not a bare overloaded `int`.
   - **C — Sparse capability profiles:** per-axis maps in `World/` (`DigProfiles`; tool data via public façade **`Tools`** = the ToolProfiles map — one public name) — composition without OOP `Item`/`Block` hierarchies.

2. **Mojang glossary (SSOT names in new code):**
   - `BlockRuntimeId` — block palette network/runtime id  
   - `ItemNetworkId` — item palette id (i16 on wire)  
   - `StackNetworkId` — ISR session prediction id (unchanged)  
   - `EntityRuntimeId` — actor/entity long id  
   - Dig hardness field = **`DestroySpeed`** (= Endstone dump `destroy_speed` / wiki hardness)  
   - Domain chest stores ≠ BDS `BlockActor` until a dedicated ADR  

3. **Unknown Survival dig:** block rid **without** `DigProfiles` entry → do **not** authenticate dig (no start / reject predict + resync). **Not** silent `DestroySpeed=2`. Creative InstantBuild unchanged. Missing profile ≠ future “unbreakable” profile (`Breakable=false` explicit later).

4. **Place vs dig:** `Blocks.IsPlaceable` allowlist independent of dig profiles.

5. **Persistence:** `SlotBlob` v2 stores kind+value; v1 migrate-on-read (`Tools.IsTool(id)` → Item, else Block). Floor cells store `StackId`.

6. **Cross-layer rule:** Overlay/`GetBlock` = `BlockRuntimeId` only. Inventory/chest/craft/floor = `StackId`. Dig = `(BlockRuntimeId, held StackId)`. Protocol is the primary place that maps `StackId` → `NetworkItemStack`. `Blocks.NormalizeMerge*` / facing normalize = `StackKind.Block` only. Never compare `.Value` across kinds.

7. **Reserved:** optional `StackUserData` (item NBT/damage) later — not in `StackId.Value`.

8. **Non-goals:** PM/DF virtual `Item`/`Block` trees; BDS BlockActor/component graph; redstone `CircuitSystem`; plugin block API; top-level `Capabilities/` folder until several axes force it (profiles stay under `World/`).

**Why:** Overloaded `InventorySlot.RuntimeId` (block **or** tool) and silent dig defaults would force a larger inventory/persist rewrite later. Sparse profiles match Dragonfly/Endstone *capability axes* without freezing Rule 7. Wire dumps stay complete; Survival honesty stays curated.

**Contributor recipe:** see [`docs/dx.md`](../dx.md) “Adding block/item capabilities”.

**Spike (same ADR era):** introduce `StackId`, migrate stores + Protocol, wrap dig behind `DigProfiles`, unknown dig policy — product dig formula unchanged (§27).

# ADR §56 — Double-chest — pair model + 54 UI (no BlockActor)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §56`.

**Choice:**

1. **Pair resolve (World):** two axis-adjacent chest cells, same Y, **same** `minecraft:cardinal_direction`. Pair axis = perpendicular to facing (N/S face → ±X neighbors; E/W face → ±Z). No BlockActor / tile-entity graph.
2. **Primary (UI left half):** lexicographically smaller `(X, Z)` of the two cells (Y equal). Wire slots `0..26` = primary cell; `27..53` = partner.
3. **Storage:** still **two** `InventorySlot[27]` keyed by cell (`StackId`). Persist remains **`ct:x:y:z`** SlotBlob v2 per cell (§39) — never a single 54-blob.
4. **Open view:** `Player.OpenChest` is `OpenChestView?` (primary + optional partner + `SlotCount` 27|54). Empty-hand (or non-sneak interact) on either half opens the **pair** UI when a partner resolves. Same-packet AuthInput sneak is read via `TryPeekMovementInput` (not only last-tick `IsSneaking`).
5. **Sneak-place:** holding a placeable + sneaking + click chest → place on clicked face (do **not** open). Non-sneak + click chest → open (even with a held item). Chest place next to a compatible neighbor forms a pair for subsequent opens (adjacency resolve — no separate pair registry).
6. **Lid:** opener refcount on **both** cells when double; BlockEvent fan-out both positions (§28).
7. **Break:** dump/dissolve clicked cell only; partner stays single 27; clear openers on both if either was open; peers with that UI open get closed.

**Why:** §46 admitted client-mesh double while server kept 2×27 — honesty gap for LAN. Pair-at-open + sneak-place matches Bedrock UX without BlockActor.

**Non-goals:** trapped/ender/copper; hopper; left/right palette `type` states unless smoke forces them; 54-slot LevelDB blob.

**Domain slots:** chest open slots are `InventorySlotReference.OpenContainer(0..53)`. (This ADR originally used flat int offsets, `ChestBase` 100 / `CraftUiBase` 200; that scheme was removed on 2026-11-13, see the audit.)

**Supersedes:** §28/§39/§46 Deferred “double-chest” lines — product leaf is this ADR.

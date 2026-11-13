# ADR §16 — Main inventory 36 slots; place stays hotbar-only

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §16`.

**Choice:** `PlayerInventory` owns one `_slots[36]` array (`FullInventorySize`). Hotbar helpers remain `IsValidHotbarSlot` 0–8 for place / MobEquipment / `TryConsumeOne`. `IsValidInventorySlot` 0–35 for Get/TrySet/TryAdd. `TryAdd` stacks then fills across the full window. Rename `SendHotbarContent` → `SendInventoryContent` (already sent window 0 length 36). No InventorySystem, CreativeContent, deep InventoryTransaction place, Actor, or effects.

**Why:** After §15, break aborted when hotbar was full even though the client UI had empty storage slots 9–35 — product gap on the Fase 3 path. Expanding storage without widening place intents avoids half-protocol race with open-inventory TX.

**Deferred:** floor drops, CreativeContent, food/effects, Actor/EventHandlers. (Click-move closed in §17.)

# ADR §73 — Floor-drop honesty — Q-throw + death loot

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §73`.

**Choice:** Close the Survival drop holes without WorldEntity/ECS.

1. **Q-throw / ISR Drop** — `InventorySystem.TryDrop` deposits into `FloorDropStore` + `AddItemActor` fan-out (`FloorDropFanout`) at the player feet cell; pickup delay **40** ticks (~2s) so the thrower does not instantly re-collect. SoftCap refuse → ISR rollback (item stays in bag).
2. **Death loot** — Survival void death dumps craft UI + bag + cursor to floor near death XZ (Y clamped to surface when below `FlatMinY` so void loot is recoverable). Creative = keepInventory. SoftCap may void remainder (Debug).
3. **Break** — inventory first; surplus → floor (already §26). A break now plans the concrete destination before it removes its source: ordinary block loot must fit the bag or a floor cell, and chest contents plus the chest item are planned together. Capacity refusal leaves the block/chest and inventory unchanged.
4. **Store** — `TryAddOrMerge` refuses overwrite when a cell holds a different id (was silent clobber).

**Addendum (ago 2026 — §98):** death loot is now one planned batch. If the complete craft-grid + bag + cursor set cannot fit, no floor drop is committed and no source slot is cleared; the dead player retains all of it for the respawn inventory resync. This replaces the old per-slot "keep the remainder" behavior.

**Clarifies:** §26 Deferred Q-throw; supersedes §40's earlier “inventory unchanged on death” behavior for Survival.

**Non-goals:** Gravity/despawn TTL; LevelDB persist of drops; XP orbs; damage pipeline deaths beyond void; always-floor-on-break (vanilla bag-first stays). Death SoftCap refusal retains the complete source set (§98).

**Status (jul 2026):** Shipped — Fanout + TryDrop + death dump + leaf tests.

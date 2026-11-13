# ADR §77 — Floor-drop despawn TTL

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §77`.

**Choice:** Close the remaining §73 gap — uncollected floor drops now disappear after a fixed lifetime, matching vanilla's 5-minute item-entity despawn, without a generic entity/TTL layer.

1. `FloorDropStore.DropSlot` gains an `AgeTicks` field (default `0`, optional positional parameter — no call-site churn). `TryAddOrMerge`/`TryTakeUpTo` explicitly carry the existing cell's `AgeTicks` forward; **merging more of the same item onto a pile does not reset its age** (a cell can't be kept alive forever by topping it off).
2. `FloorDropStore.TickDespawn(expired, tickDiff, despawnTicks = DefaultDespawnTicks)` ages every cell once per `BlockSystem` tick (same two-phase scratch-list pattern as `TickPickupDelays`, to avoid mutating `_drops` mid-iteration) and removes + reports cells that cross `DefaultDespawnTicks` (6000 ticks / 5 min at 20 TPS).
3. `BlockSystem.PickupFloorDrops` sends `RemoveActor` for each expired cell to peers who are in-game or already know the chunk (same visibility rule `FloorDropFanout.Publish` uses) — no bag mutation, the item is just gone, as in vanilla.

**Clarifies:** §73 "Non-goals: ... floor-drop despawn TTL" (that gap is now closed); `roadmap.md` Yes-next priority 3.

**Non-goals:** Per-item despawn time variance (vanilla varies this for some items/enchants — out of scope); despawn timer reset on player proximity or re-throw; LevelDB persistence of drop age across restart (drops are already RAM-only per §26); a generic `Entity`/TTL component — this stays a `FloorDropStore`-local concept.

**Status (ago 2026):** Shipped — `AgeTicks` + `TickDespawn` + `BlockSystem` wiring + leaf tests (`FloorDropStoreTests`).

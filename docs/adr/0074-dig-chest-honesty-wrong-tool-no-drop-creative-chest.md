# ADR §74 — Dig/chest honesty — wrong-tool no-drop + Creative chest dump

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §74`.

**Choice:** Close the two LAN “looks like a bug” gaps left after §73 without opening vitals/TTL/Destroy.

1. **Wrong-tool no-drop** — Survival break still removes the cell when dig auth passes. If `DigProfiles.RequiresCorrectToolForDrops` and `!BreakDuration.IsHarvestable(held)`, skip bag/floor for the **broken block rid**. Soft blocks / chest block item still drop with empty hand. Chest **contents** dump unchanged.
2. **Creative chest contents** — InstantBuild still skips dropping the chest **block**. `RemoveAndDump` contents always go to `FloorDropFanout` (Creative does not TryAdd to bag). Clarifies §31 “clears chest store” ≠ void.
3. **Destination SoftCaps** — death loot remains §98's all-or-nothing plan. Phase II applies the same concrete preflight principle to ordinary block drops and chest contents: inventory/floor refusal rejects before the world mutation, so no accepted break can silently lose its decided loot.

**Clarifies:** §27 Deferred wrong-tool loot; §31 Creative break; §73 SoftCap death void.

**Non-goals:** Vanilla loot tables (cobble from stone); tool durability; Creative Destroy ISR; floor despawn TTL; generic transaction or loot framework.

**Status (ago 2026):** Shipped — loot gate, Creative chest dump, bounded-destination preflight for break/chest, and leaf tests.

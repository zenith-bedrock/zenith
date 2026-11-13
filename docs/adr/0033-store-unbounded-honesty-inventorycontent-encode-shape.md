# ADR §33 — Store unbounded honesty + InventoryContent encode-shape

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §33`.

**Choice:** Document Known debt on §26 / §28 (unbounded dicts). Add an **encode-shape** test for outbound `InventoryContentPacket` in the style of existing inventory packet tests — do **not** implement `Decode` (packet remains outbound-only).

**Why:** ARCHITECTURE already documents unbounded `_blockOverrides`; chest/floor stores match that pattern and should say so. Round-trip wording would force useless Decode stubs.

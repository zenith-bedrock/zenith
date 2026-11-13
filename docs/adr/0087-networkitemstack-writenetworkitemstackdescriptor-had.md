# ADR §87 — `NetworkItemStack.WriteNetworkItemStackDescriptor` had the same class of bug — fixed at the root

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §87`.

**Choice:** After §86 unblocked the join sequence further, `debug:capture` surfaced `InventoryContentPacket` failing to decode ("offset out of range") whenever a slot carried a non-zero item-stack network id. Traced to `NetworkItemStack.WriteNetworkItemStackDescriptor` — shared by `InventoryContentPacket` **and** `MobEquipmentPacket` — writing an extra `stack_id_variant` tag `uvarint32(0)` between the `has_stack_id` bool and the `stack_id` value itself. The real `ItemV4` shape (confirmed against `minecraft-data`'s 1.26.40 schema) has no such tag: `has_stack_id`(bool) then a bare `stack_id`(`zigzag32`) if true, directly.

1. **Fixed once, at the shared method** rather than patching each call site — exactly the "fix at the root" the roadmap's suggested grouping for `PlayerAuthInputPacket`/`ItemStackRequestPacket`/`ItemStackResponsePacket` already anticipated (those three share this same item-stack-net-id-variant concern per that row's note); this fix now also covers `InventoryContentPacket`/`MobEquipmentPacket`, which weren't in that original grouping but turned out to share the exact same bug through the same method.
2. **Not exercised by the smoke bot until now** because it only manifests when a slot actually carries a non-zero stack net id (ISR-tracked item identity) — an air/empty-net-id inventory happens to skip the buggy branch entirely, which is likely why this went uncaught through §83's earlier `first10` runs (that never got far enough to send a populated inventory) and through unit tests (none exercised the `hasNetId == true` branch end-to-end against a real decoder).

**Verification:** `debug:capture` against a live local server — `inventory_content` now decodes cleanly even with populated, ISR-tracked slots. Added `NetworkItemStackDescriptorTests.cs` (both branches, byte-level, `IsEndOfFile`-asserted).

**Non-goals:** `WriteItemStack`/`WriteSerializedNetworkItemStackDescriptor` (already verified correct in §82/§85); `PlayerAuthInputPacket`/`ItemStackRequestPacket`/`ItemStackResponsePacket`'s own migration (still open — but now share a verified-correct helper once they're tackled, one less thing to get wrong there).

**Status (ago 2026):** Shipped — `WriteNetworkItemStackDescriptor` tag byte removed, verified live, 2 new tests, 514/514 green.

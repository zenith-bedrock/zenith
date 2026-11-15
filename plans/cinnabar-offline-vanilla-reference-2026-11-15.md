# Cinnabar offline as a vanilla behavior reference

Inspected read-only on 2026-11-15: cinnabar-reference commit 7ddef27. This describes the checked-out source; the installed executable's exact build was not established. No client source, fixtures or assets were copied into Zenith, and no user world or process was changed.

## Confirmed runtime

The normal local-world path runs the official Bedrock Dedicated Server. The launcher provisions the executable, links the chosen world, writes server.properties and launches BDS. Local play uses the ordinary client/core session path. There is no independent Rust implementation of authoritative vanilla combat, loot and recipes to transplant.

Evidence:

- cinnabar-reference/docs/local-worlds.md: normal worlds use BDS; legacy Dragonfly saves require conversion, with no fallback.
- cinnabar-reference/crates/launcher-host/src/local_worlds/bds.rs: BdsRunner starts the provisioned executable, with offline authentication and NetherNet transport.
- cinnabar-reference/assets/bedrock-target.json: checked-out target is game 1.26.60, BDS 1.26.60.6, wire protocol 2225. Zenith targets 1.26.62/protocol 2225; shared wire version does not prove identical gameplay behavior across patches.
- cinnabar-reference/docs/experience-runtime.md: the development local-server tool is not the packaged player local-world backend.

“Offline” also has distinct meanings: offline authentication is configured, but desktop BDS still needs viable ICE routes. The local-world documentation explicitly records a no-network desktop limitation. Do not treat disconnected networking as guaranteed by local play.

## Useful distinctions for Zenith

### Combat

The client's survival attack cap is 3 blocks from the eye; creative has a different cap and input-mode picking constraints. Selection ray-tests actor hitboxes and accounts for intervening blocks. Sources: crates/client-world/src/game_mode_capabilities.rs; crates/gameplay/src/melee/target.rs and its picking tests.

Zenith's new 3-block eye-to-AABB check agrees with the survival cap, but does not by itself reproduce ray selection, client interpolation, creative behavior or the BDS server's acceptance tolerance. Client pick limits and server validation limits must be tested separately. Do not declare combat parity from matching one constant.

### Crafting close

The client stages inventory transactions that return cursor/grid ingredients to compatible stacks and empty inventory slots, then drops overflow. Cleanup requests precede window close; pending replies still settle the old generation. Source: docs/reference/inventory-crafting-close.md.

Zenith must accept the valid cleanup sequence and conserve inventory across close/reopen and rejected requests. A second unconditional server-side return could duplicate a client-initiated cleanup. ADR 139's missing return behavior therefore needs a handler-to-tick sequence test before choosing the fix.

### Pickup

The client reference records local BDS pickup authority arriving through a normal InventoryTransaction, without InventorySlot/InventoryContent for that observation. TakeItemActor alone does not establish the inventory count. Source: docs/reference/inventory-normal-transactions.md. This is an observed path, not a requirement that Zenith emit exactly that one packet form.

Check both actor disappearance and accepted authoritative stack identity/count, including the next placement/drop. A visible pickup is insufficient evidence of inventory correctness.

## Controlled comparison procedure

Use a fresh isolated BDS world and an isolated Zenith fixture, with the same Cinnabar executable, input mode, game mode, held items and transport. Preserve user worlds. Record exact BDS patch versions; repeat on Zenith's target patch when they differ. Capture our own observations rather than importing reference fixtures.

1. Combat: first click at several distances, vertical offsets and behind a block; rapid clicks and a target moving into range after a rejected click. Compare action arrival, accepted damage, knockback and invulnerability separately.
2. Loot: break the same block with the same tool; record ground actor, pickup delay, inventory count/identity and the next use. Repeat with full inventory and a chest containing items.
3. Crafting: fill a workbench, take one result, close with ingredients/cursor occupied, reopen, and repeat with full inventory. Record cleanup and close order, responses, slot identities and total item conservation.

Done criteria: every accepted/rejected action and resulting item total is explained by the captured sequence; differences are documented as intentional or reproduced by a failing Zenith regression test. Current status: source inspection complete; these controlled live comparisons have not been performed in this turn.

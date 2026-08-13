# Phase IX — World gameplay systems foundation findings

## Audit result

The requested Phase IX primitives are already present in the current Zenith runtime. The audit
therefore closes this phase without rebuilding them or introducing a new framework.

| Requirement | Current concrete authority | Verified behavior |
|---|---|---|
| Item identity/count/comparison | `StackId`, `InventorySlot`, `PlayerInventory`, `ItemPalette` | discriminated block/item identity, bounded stacks, exact item comparison, palette-backed reverse lookup |
| Wire serialization | `InventoryProtocol`, `NetworkItemStack`, Packets | gameplay stacks are projected at Protocol; packets remain serialization-only |
| World item lifecycle | `FloorDropFanout`, `FloorDropStore`, `FloorDropSystem` | authoritative deposit, pickup delay, partial/full pickup, remove/republish, despawn and interest projection |
| Containers | `ChestStore`, `InventorySystem`, `OpenContainerSession` | open/close, paired chests, bounded reach, generation-checked transactions, lid replication |
| Persistence | `World`, `IChunkStorage`, `SlotBlob` | player/inventory, block overlays and chest contents survive restart; transient floor drops remain RAM-only by policy |

## Reference audit

Local sources under `D:\Development\bedrock` were inspected for behavior, not copied:

- Dragonfly separates item identity/inventory from world item entities and gives item entities
  pickup delay, partial collection and lifetime behavior. Zenith already has the useful concrete
  subset in `FloorDropStore` and keeps ownership in `FloorDropSystem`.
- PocketMine models item stacks through an item factory and has broad block-inventory/tile
  surfaces. This confirms the importance of separating stack identity from container ownership,
  but its generalized object/plugin surface is beyond Zenith's current need.
- BetterAltay exposes a similarly broad Item/Inventory/Tile hierarchy; it remains a useful
  counterexample against introducing a generic item or container API prematurely.
- Basalt and Endstone expose larger server/item/entity surfaces. Their existence does not provide
  evidence that Zenith needs an entity hierarchy, plugin item API or data-driven framework now.

The existing Phase I/II audits contain the detailed protocol, smoke and conservation evidence;
this phase records the closure decision and current contract map rather than duplicating those
reports.

## Proven invariants

- Gameplay is the sole authority for inventory, world, chest and floor-drop mutation.
- A lethal mob transition is preflighted against the bounded drop destination; a failed drop does
  not silently remove the mob's decided loot.
- Pickup updates inventory before publishing the resulting actor removal/partial remainder.
- Competing players cannot both consume the same floor-drop slot.
- Placement, block breaking and chest destruction preflight inventory/floor destinations before
  committing world mutation.
- Player inventory and chest state persist through the existing storage keys and restart path.
- Floor drops are intentionally transient; persisting them would be a separate lifecycle decision.

## Decision

**Keep the current concrete model.** No `ItemEntity`, loot-table engine, container framework,
generic transaction layer, entity hierarchy, plugin API, ECS or data-driven item framework is
justified by the current evidence.

The next useful gameplay work should be a concrete feature that creates new pressure. Only after
repetition is observed should shared item operations, world-query seams or transaction contracts
be extracted.

## Validation evidence

The repository already contains focused tests for palette lookup, non-tool item projection, item
stack transactions, competing pickup, mob loot, chest pairing, persistence and no-loss destination
preflight. The Phase I/II smoke records cover Zombie loot, inventory, chest open and LevelDB restart
proof. Full build/test validation is rerun as part of this closure.

No production source change was required for Phase IX; this document is the audit/decision artifact.

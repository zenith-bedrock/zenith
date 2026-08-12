# Phase I — Gameplay systems foundation audit

## Current Zenith baseline

The intended Phase-I primitives already exist in a deliberately small form:

| Concern | Existing authority | Evidence / boundary |
|---|---|---|
| Stack identity and count | `StackId` + `InventorySlot` | Block and item identities are discriminated; bare runtime ids are forbidden. |
| Stackability and comparison | `PlayerInventory` | Exact item identity merges; block stacks use the existing block merge rule. |
| Wire serialization | `InventoryProtocol` | Gameplay supplies `InventorySlot`; Protocol maps it to `NetworkItemStack` and packets serialize it. |
| Persistence | `SlotBlob`, `World`, `IChunkStorage` | Player inventories use `inv:`, chests use `ct:`; blobs are versioned and slot-kind-aware. |
| World drops / pickup | `FloorDropStore`, `FloorDropSystem`, `FloorDropFanout` | Tick-owned delay/despawn/pickup, AABB reach, partial pickup, actor replication and inventory persistence. |
| First container | `ChestStore`, `InventorySystem` | Chest open/session/ISR transactions, lid replication and persistent blobs are already concrete. |

`StackId.FromItem` can already carry a palette network id, but `InventoryProtocol` currently maps
only curated tool item ids. That is the smallest real item-runtime gap for concrete non-tool mob
loot such as rotten flesh, bone or arrow. NBT, durability, enchantments and generic item objects
are not needed to close it and remain deferred.

The substantive gameplay gap is mob loot: Player death already drops all authoritative inventory
atomically, but Zombie death creates no drop and Skeleton has no damage/removal path. Floor drops
are RAM-resident by deliberate prior policy; player inventory and chest persistence are already
implemented. Any request to persist world drops needs its own explicit storage/lifecycle decision,
not an accidental extension of `FloorDropStore`.

## Reference comparison

The local references were used for behavior, not copied architecture:

- Dragonfly item entities cap stacks, model a pickup delay and five-minute lifetime, support partial
  collection, and use item comparability before merge. Zenith already has those concrete semantics
  through `FloorDropStore`/`FloorDropSystem`, including partial pickup and authoritative remove plus
  republish. Dragonfly's generic world entity/collector/behavior model is intentionally out of
  scope for Zenith.
- Dragonfly maps item identity through a registry name plus metadata and serializes NBT only at
  item persistence/wire boundaries. Zenith should retain its smaller `StackKind + network id`
  representation until a real item feature needs metadata/NBT.
- BetterAltay's `Item` carries broad NBT, enchantment, creative and plugin-facing behavior. It is a
  useful reminder to keep item identity distinct from wire/network ids, but is not a suitable
  architecture to transplant: its object hierarchy and extension surface exceed Phase I.

## Direction resulting from the audit

1. Do not rebuild inventory, pickup, chest or persistence foundations already present.
2. Add the narrow palette-backed non-tool item mapping needed by the first real drops.
3. Implement explicit, concrete Zombie and Skeleton death drops; do not introduce a loot table or
   generic combat/mob layer.
4. Prove server-side conservation, competing-player pickup, relevant-observer replication and
   persistent inventory using leaf/multiplayer tests plus Bedrock smoke.
5. Add diagnostics only for committed facts (for example successful/rejected pickup and completed
   mob-drop creation) through fixed composition-root handles, after the concrete operations exist.

## Phase-I implementation result

The audit changed the implementation direction rather than opening a new item runtime:

- `ItemPalette` now has a reverse network-id lookup. `InventoryProtocol` uses that palette mapping
  for `StackId.Item`, so palette items which are not tools can cross the existing protocol boundary
  as `NetworkItemStack`. Packets remain wire-only; no item object hierarchy or metadata carrier was
  introduced.
- `ZombieSystem` creates exactly one concrete `minecraft:rotten_flesh` floor drop on a lethal
  transition. `SkeletonSystem` now owns its matching concrete health/removal path and creates one
  `minecraft:bone` drop. The systems retain their separate behaviors; this is not a common mob,
  combat or loot-table layer.
- Lethal transitions preflight the bounded floor-drop store. If the drop cannot be committed, the
  mob remains alive and unchanged. This makes the invariant explicit: a death does not silently
  destroy its decided loot. A prevalidated commit is still guarded as an invariant violation.
- Existing `FloorDropSystem` performs the delayed, authoritative AABB pickup, inventory update,
  item-actor removal/partial republish and inventory persistence. The new Skeleton test exercises
  that path end-to-end after the concrete death drop.
- Existing fixed diagnostics already provide contextual timing for `zombie`, `skeleton`,
  `floor-drop` and `inventory`, plus tick allocation, packet bytes and datagram facts. No per-item
  metric names were added speculatively: the current evidence supports observing the owning systems
  and network cost, while a product question that needs accepted/rejected pickup counts can add
  fixed composition-root handles without making diagnostics a gameplay dependency.

## Evidence

- Focused leaf tests cover reverse palette lookup, wire projection of a non-tool item, Zombie and
  Skeleton loot, delayed pickup into inventory, and refusal of a lethal transition at the
  `FloorDropStore` SoftCap. The tested conservation rule is: a full store leaves the mob health and
  active state unchanged rather than producing a death without its loot.
- The ItemStack request/response wire has been reconciled with the imported 2169 Cereal definition:
  request ids are little-endian `int32`, action discriminators are one `uint8` (not a legacy
  discriminator pair), and response required/optional markers are emitted at both levels. The
  byte-level packet tests cover this shape, preventing the external client from being rejected
  before gameplay can own an inventory intent.
- The two-client Zombie Bedrock smoke now waits for an `add_item_entity` near the observed death
  position as well as health and remove events; live `smoke:zombie`, `smoke:inv-hotbar`, and
  `smoke:chest-open` runs passed against the local server. This keeps the external proof at the
  protocol boundary without teaching gameplay about packets.
- A new two-client floor-pickup harness reached real drop creation after the Cereal correction but
  does not yet observe `take_item_entity` reliably in its synthetic full-bag scenario. It is not
  treated as proof of a runtime fault: existing multiplayer/unit tests cover the authoritative
  competing-player pickup path. Keep the harness as a follow-up until it can assert the complete
  external animation without synthetic inventory assumptions.

## Deferred

Item metadata/NBT, custom stack equality, durability, enchantments, generalized containers,
world-drop persistence, behavior trees and an actor hierarchy are not evidenced by this work.

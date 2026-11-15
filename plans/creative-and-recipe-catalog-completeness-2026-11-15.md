# Post-impl audit

**Scope:** develop working tree, catalog completeness (ADR 156). Existing protocol/inventory work is preserved.
**Verdict:** Pass, with real-client visibility confirmation pending.

## Blockers

- None found in this round's review.

## Nits (non-blocking)

- Broad item visibility does not certify every block's physics, workstation behavior, item stack limit or enchantment effect.
- All registered grid recipes are initially visible as an explicit Zenith policy while progressive discovery is absent; this differs from BDS progression.
- Bundled catalog provenance/version remains as recorded in data/manifest.json. This change uses those existing resources and does not claim a fresh target-patch capture.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| Gameplay/Inventory/CreativeItemsLoader, CreativeCatalog | Resolve source identities including aux and NBT; keep all catalog entries | Yes |
| World/ItemUserData, StackId | Immutable metadata participates in identity; wear changes retain other tags | Yes |
| World/SlotBlob, ChestStore; Player/PlayerInventory | Persist metadata in v5, read v1-v4, keep ordinary v4 layout | Yes |
| Protocol/CreativeContentBuilder, InventoryProtocol | Project complete identities and initialize recipe-book visibility | Yes |
| Packets/NetworkItemStack | Serialize retained metadata in the existing item extra-data format | Yes |

## Freeze / anti-pattern scan

- [x] No new scheduler, DI, ECS, plugin interface or Network/ catch-all.
- [x] Gameplay mutation and request ordering remain on the tick.
- [x] No copying from Cinnabar: data comes from Zenith's existing embedded catalog.
- [x] Stack equality includes immutable tag contents; variants do not silently collapse.
- [x] Existing pinned creative IDs stay assigned to the first matching exact identity; other entries receive distinct IDs.

## DX / tests

- Existing registry: 3,819 grid recipes (1,149 shaped, 2,670 shapeless). All are now initially announced in the recipe book; ingredient, shape and station checks remain active.
- Creative source: 1,980 entries, previously filtered to 45. Aux variants and 175 metadata entries are retained; runtime display categories come from the same source.
- Leaf tests in zenith.Tests cover complete source-to-catalog/wire identities, potion and enchanted-book variants, creative grant to inventory, inventory/chest persistence, item-wire NBT, preserving tags while changing wear, malformed v5 lengths/truncation without partial destination writes, and full recipe-unlock round-trip. Existing v4/wear golden tests remain intact.
- Managed immutable metadata makes InventorySlot a managed value; four load-path stackalloc buffers become small managed arrays. No per-tick buffer refactor or pooling framework is added.
- Focused validation: 112 tests passed. Full Release solution: all 1,804 tests passed (1,499 main project + 305 leaf projects), no failures/skips. Diff whitespace check passed.

## Should not be committed

- Builds/ server runtime logs, configuration, binaries and world remain ignored.
- Earlier dirty changes have not been discarded or automatically committed.

## Suggested follow-ups

1. Reconnect Cinnabar and verify recipe-book and creative categories, potion variants and enchanted-book tooltips.
2. Implement unsupported item/station mechanics and progressive discovery as separate concrete gameplay work; do not equate catalog exposure with full vanilla parity.

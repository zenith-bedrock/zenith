# Architecture and DX audit (2026-11-13)

Taken after the `Player` split (`5d645d9`). Method: reading code, greps and one full test run (1341/1341). **Not verified:** no live server run and no smoke run for this audit; no retail client. Findings are open work unless marked otherwise.

## What holds up
- `ArchitectureBoundaryTests` pins, at IL level: Packets reference no domain/protocol layer; Gameplay references no wire models, `Session` or `Protocol`.
- Nothing under `Packets/` references `Player`. Gameplay has no real `lock` (concurrency stays at the handoff edges).
- Largest domain file is `InventorySystem` (694 lines). `Player` went from 947 lines to identity/pose/inventory/vitals plus `Intents`, `Dig`, `Windows`, `Effects`, `Replicated` (`docs/architecture.md`, "Player").
- Build has 0 warnings.

## Findings, most serious first

1. **A test-only parallel API lives in production code.** The flat-slot scheme (`InventorySlotReference.TryFromLegacyFlat`, `InventoryContainerMap.ChestBase=100`/`CraftUiBase=200`/`CraftResultFlat`, `InventoryStackIntent`'s `Legacy(flat)` helpers, the `int flat` overloads of `DescribeForWire`/`MatchesAdvertisedStackNetId`) is documented as "compatibility only for characterization tests" yet is called from 6 places in `src/zenith` (4 in `InventoryProtocol`). It contradicts the no-legacy rule and leaves two ways to name a slot. Fix: migrate the tests (about 20 direct uses plus the helper callers) to `InventorySlotReference`, delete the flat API.
2. **The test suite is serial by configuration.** `src/zenith.Tests/xunit.runner.json` has `parallelizeTestCollections: false`; summed test time (139 s) is almost the wall time (2m16s). Three terrain tests alone take about 24 s. First find the shared static state or timing that motivated the flag, then enable parallelism (this is the way into open item 6). The lifecycle race fixed in `5d645d9` is the kind of test the serial mode hid.
3. **`IntentContractTests.cs` is 3957 lines / 159 tests in one file.** Split by component (`PlayerIntents`, `PlayerDig`, `PlayerWindows`, ...) to match the new structure.
4. **Stale text after ADR §149.** `ChunkSpatialIndex`, `GroundMobMovement`, `FishSystem` still mention a "legacy actor/roster" and `IDamageableActor`/`FishStore`, which no longer exist. `docs/decisions.md` entries (§131 and others) describe `GroundMobCombat`/`IDamageableActor` without marking them superseded.
5. **`Player` surface.** `PlayerWindows.OpenChest` and `InventoryWindowOpen` still describe themselves as "compatibility projections": make them real API or remove them (count callers first). `Player` has 19 public setters (pose, vitals, flags) that by the single-writer rule only the GameLoop should write, with nothing enforcing it.
6. **Decision records.** 253 "ADR §" references in code; `docs/adr/` holds 3 files while `decisions.md` (3548 lines) holds the rest in one document. Hard to navigate and maintain.

## Suggested order
1 and 4 together (one commit), then 2 and 3, then 5, then 6.

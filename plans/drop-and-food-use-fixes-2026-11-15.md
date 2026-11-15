# Post-impl audit

**Scope:** develop working tree; G/world drop and food consumption (ADR 157). Earlier protocol/catalog changes are preserved.
**Verdict:** Pass with documented gameplay limitations; manual client test pending.

## Blockers

- None found in this round's review.

## Nits (non-blocking)

- Food-specific poison, absorption, regeneration and chorus teleport remain outside the nutrition/use lifecycle implemented here. This does not claim full special-food parity.
- Durations and per-food facts are reference-based, not newly measured against the target BDS patch.
- Server intentionally remains stopped. No live client confirmation is claimed.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| Session/Handler/InGameInventoryHandler | Accept canonical whole-stack legacy drops without an ID; bake slot preconditions; publish food cancellation | Yes |
| Player/InventoryStackIntent | Immutable original-slot precondition and response policy for legacy drops | Yes |
| Gameplay/Inventory/InventorySystem | Revalidate source before commit; preserve snapshot rollback and correlated replay checks | Yes |
| Player/FoodUseState, PlayerIntents, Player | Stack-bound active use and bounded start/cancel handoff | Yes |
| Gameplay/Survival/FoodItems, HungerSystem | Nutrition, timing, cancellation, exact-slot consume and remainder conservation on tick | Yes |
| Gameplay/Survival/MovementSystem | Preserve food-use state when projecting pose flags | Yes |
| Protocol/EntityProtocol; Packets/metadata | Project using-item flags, including late join; no food decisions in packets | Yes |
| Packets/ActorEventPacket; Session handler | Decode client progress without granting inventory/health authority | Yes |

## Freeze / anti-pattern scan

- [x] No new scheduler, ECS, DI or generic dispatcher.
- [x] Network only submits food-use intents; the hunger tick owns active use and consumption.
- [x] FoodUse ownership is declared in ArchitectureBoundaryTests for HungerSystem and Player death finalization.
- [x] Start/cancel FIFO is bounded at eight entries; cancel remains latched if start spam fills it.
- [x] No copying from reference implementations; behavior/facts informed the original C# changes.
- [x] No synchronous persistence wait or new configuration/environment switch.

## DX / tests

- G/drop: ID-less whole-stack transaction queues successfully; repeated input cannot double-drop; a different replacement stack is preserved. Correlated transactions retain their response IDs/replay ledger. Empty-source drops get inventory correction, without inventing an ItemStackResponse ID.
- Food: ordinary use waits 32 server ticks, dried kelp 16, honey 40. Repeated starts do not extend the same use. Release, changed selection/stack, death or departure cancel it. Only the selected original stack is consumed.
- Forty food definitions replace the original eleven, with individual nutrition/saturation and always-edible exceptions. These are facts from item references, not a new item-component framework.
- Bowl/bottle returns replace a final held item; overflow from a nonempty held stack is inserted into inventory or deposited on the floor. Failed floor admission restores the inventory and does not grant nutrition.
- Client ActorEvent progress is presentation, not a completion signal; no client event can accelerate authoritative food completion.
- Leaf tests cover handler-to-tick start/release, duration, selected-slot conservation, replay/stale drops, full hunger exceptions, container returns/overflow, cancel under queue pressure and metadata flags. Existing creative inventory behavior remains covered.
- Final `dotnet test zenith.sln -c Release --no-restore -m:1`: all 1,820 tests passed (1,515 main project + 305 leaf projects), no failures or skips. Diff whitespace check passed. The new FoodUse property is included in the executable ownership table rather than exempted from it.
- Shutdown verified: zero zenith processes, zero UDP 19132 listeners and zero TCP 19133 listeners. Runtime bookkeeping marks the server stopped; the LevelDB world is preserved.

## Should not be committed

- Builds/ runtime logs, configuration, binaries and the preserved LevelDB world remain ignored.
- No commits/push or reference-repository edits were performed in this round.

## Suggested follow-ups

1. When the user chooses to restart, test G with one item and a partial stack, inventory-screen drops, early food release, held eating and slot switching on both transports.
2. Implement special-food effects only with explicit, tested behavior; validate exact timings against an isolated BDS target patch.

References: inspected Cinnabar inventory legacy/drop and item-use/progress code read-only; food facts cross-checked against [PocketMine item definitions](https://github.com/pmmp/PocketMine-MP/tree/stable/src/item), including [Carrot](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/Carrot.php), [SweetBerries](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/SweetBerries.php) and [GoldenApple](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/GoldenApple.php).

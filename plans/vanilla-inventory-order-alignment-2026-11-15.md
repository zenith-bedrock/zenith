# Post-impl audit

**Scope:** develop working tree; this round changes inventory lifecycle ordering and mode-specific melee reach. Earlier protocol fixes remain present and are not reclassified as new changes.
**Verdict:** Pass, with live-client comparison pending.

## Blockers

- None found in this round's changes.

## Nits (non-blocking)

- Live Cinnabar/BDS differential acceptance has not been performed for this refactor. Regression tests verify Zenith's handler/tick contracts, not all vanilla behavior.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| Player/PlayerWindows | One bounded FIFO for inventory requests and windows; reserve admission generations under lock | Yes |
| Player/PlayerIntents, Player | Delegate inventory submission to its lifecycle owner | Yes |
| Player/InventoryWindowIntent | Immutable queued lifecycle facts, including reserved generation | Yes |
| Session/Handler/InGameInventoryHandler | Bind requests to projected queued session; submit without mutating inventory | Yes |
| Gameplay/Inventory/InventorySystem | Apply requests in order on tick; retain rollback/replay/stack validation | Yes |
| World/EntityHitboxes, Gameplay/Entities | Mode-specific player attack range shared by mob and player targets | Yes |
| Player/PendingMailbox | Remove obsolete last-entry peek after the unified inventory queue replaces it | Yes |

## Freeze / anti-pattern scan

- [x] No new frozen abstraction; ADR 155 records the concrete lifecycle queue refactor.
- [x] No Network/ catch-all or packet/domain leakage introduced.
- [x] No handler world/inventory mutation. Active sessions remain tick-owned; generation reservations and admission projection are handoff state.
- [x] Discrete work is FIFO. Existing limits remain four queued windows and eight queued transactions per player; overflow rejects the newest request of that kind.
- [x] Locks cover queue state and admission snapshots, never protocol sends or disk I/O.

## DX / tests

- Tests in zenith.Tests cover personal/table/cursor cleanup, overflow drops, close/reopen in one tick, replay, stale generations, refused chest open, generation visibility while applying an open, and survival/creative combat ranges for mob and player targets.
- Opens reserve generations. Requests after a queued close cannot claim the old window; requests for refused opens cannot fall through to an older chest. A subsequent open can establish its reserved generation even if the same UI is still visible, without sending a duplicate ContainerOpen.
- Client cleanup is applied once before close. No second automatic grid return is added.
- Survival actor cap remains 3 blocks; creative uses 7, as observed in Cinnabar's mode capabilities. Ray picking, client input-mode limits, interpolation tolerance and exact BDS server acceptance remain distinct unresolved parity questions.
- Final `dotnet test zenith.sln -c Release --no-restore -m:1`: all 1,799 tests passed (1,494 main-project tests plus 305 leaf-project tests); no skips. Diff whitespace check passed.
- Restarted the Release server as PID 43380, preserving the existing LevelDB world. Connection/startup verification is recorded with the runtime logs under ignored Builds/zenith-test-runtime/.

## Should not be committed

- Builds/ runtime state, logs, binaries and persistent world are ignored.
- This round does not discard or overwrite earlier dirty work.

## Suggested follow-ups

1. Compare Cinnabar cleanup/close/reopen and combat against an isolated BDS of the target patch.
2. Establish behavior for a close without any client cleanup, rejected cleanup followed by disconnect, and shaped recipe validation before changing those separate contracts.

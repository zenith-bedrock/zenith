# Zenith — Audit Implementation Status

Status as of `44304de` (2026-08-11). This document records the implementation outcome of the architecture and inventory audits; it does not rewrite their original evidence.

## Result

The runtime remains a single ordered `GameLoop` plus small `IGameSystem`s. The changes remove shared writers from authoritative dig, inventory/session, spawn, and chunk-completion paths without imposing a tick handoff on independent networking, storage, logging, or compression work.

| Finding | Status | Implemented outcome |
|---|---|---|
| ZAR-001 | Resolved | Network submits immutable dig intents; `BlockDigSystem` is the gameplay owner of break state. |
| ZAR-002 | Resolved | Chunk and pre-spawn I/O publish bounded completions; the tick revalidates and applies them. |
| ZAR-003 | Resolved | Chest actions revalidate active-session authority and reach at mutation time. |
| ZAR-004 | Resolved | Architecture boundary tests protect packet/gameplay dependency rules. |
| ZAR-005 | Resolved | A game-system exception is logged, stops authority, and is propagated to the server shutdown lifecycle. |
| ZAR-006 | Resolved | Empty floor-drop ticks avoid the previous unconditional scan/allocation. |
| ZAR-007 | Resolved | Dig, block edit, and floor-drop lifecycle are separate systems with explicit ordering. |
| ZAR-008–010 | Resolved | Test-only tick overloads are gone, equipment runs after mutations, and pre-spawn chat is retained. |
| ZAR-011 | Deferred, bounded | Active falling blocks are capped at 512. Per-entity-per-tick simulation and viewer replication are necessary for current physics; introduce a work cap only after a measured scale requirement. |
| ZAR-012 | Resolved/documented | Direct session-lifecycle scalars remain explicit narrow exceptions, not gameplay ownership escapes. |
| ZAR-013 | Verified | Gameplay persistence calls are fire-and-forget; the tick does not wait for a storage put. |

| Finding | Status | Implemented outcome |
|---|---|---|
| ZIA-001 | Resolved | `OpenContainerSession` binds protocol window, generation, and domain target per player. |
| ZIA-002 | Resolved | `InventorySlotReference` and `InventorySlotResolver` separate wire coordinates from domain location. |
| ZIA-003 | Resolved | Stale/closed/distant chest sessions cannot mutate storage. |
| ZIA-004 | Resolved for shipped ISR | Request replay, session generation, and session-scoped stack identity are validated before commit. |
| ZIA-005 | Open by design | Runtime commits are atomic; persistence is asynchronous and not a durable multi-key transaction. Trade needs an ADR before claiming durable atomicity. |
| ZIA-006 | Resolved by restraint | Slot resolution stays location-only; no strategy/registry framework was introduced without a second real behavior. |

## Correctness properties protected

- Client wire input is never item authority; it is validated against authoritative slots and stack tokens.
- A successful inventory request has one tick-owned commit; rejected or partially failing requests restore snapshots and publish no partial floor drop.
- Replayed request ids, stale stack ids, and stale container generations do not commit.
- Spatial chest authority is rechecked at action time; disconnect/death/container close release the active session on tick.
- Death is one-shot: repeated dead ticks cannot mint the same loot again.
- A failed game system fails closed rather than allowing later systems to run against potentially partial state.

These are supported by focused inventory rollback/replay/conservation tests, lifecycle tests, architecture-boundary tests, `GameLoopTests`, and the death-loot regression. They are not a blanket claim that every future feature is “anti-dup”; each new valuable state transition still needs its own stated invariant and test.

## Critical-state ownership matrix

| State | Authoritative owner | Cross-thread input/result | Stale/replay protection | Remaining boundary |
|---|---|---|---|---|
| Inventory and open containers | GameLoop / `InventorySystem` | bounded stack/window intents | request id, session generation, scoped stack id | durable multi-key commit for future Trade |
| Player movement, health, death/respawn | GameLoop / `MovementSystem` | overwrite-latest movement, one-shot respawn | dead-state gating and consumed respawn request | no persistence transaction needed today |
| World block mutation and dig | GameLoop / `BlockDigSystem` + `BlockEditSystem` | bounded dig/edit intents | dig authorization and tick timing | future combat/AI must use the same ownership review |
| Floor drops | GameLoop / `FloorDropSystem` | none | transactional inventory pickup/drop planning | RAM-only crash-soft persistence is known debt |
| Chunk/spawn lifecycle | GameLoop / `ChunkStreamSystem` | storage completion handoff | tracker epoch, session/connection revalidation | benchmark under large view distances |
| Connection flags | Session lifecycle | direct same-session transition | documented exception; not gameplay authority | keep narrow and explicit |
| Storage | storage worker | fire-and-forget writes, shutdown flush | storage implementation tracks pending writes | crash may lose recent writes by design |

## TradeInventory fitness result

**Today: PARTIALLY.** A new single-owner container can use the session and slot-resolution boundaries without changing packet decoding or adding gameplay branches to `InGameInventoryHandler`. A two-player `TradeInventory` is not cleanly implementable yet because there is no approved multi-inventory atomic commit or durable multi-key boundary.

The smallest justified next step is not a generic framework: approve one real single-owner container (for example Mailbox) and prove it through the current session seam. Only when Trade is approved should Zenith add an explicit tick-owned `TryCommitTrade` that snapshots every participant, validates all participants, commits all-or-nothing, and cancels on lifecycle change. It must be preceded by a durability ADR if a completed trade must survive crash atomically.

## Verification

- `dotnet test zenith.sln --no-restore` — **676 passed**: 564 `zenith.Tests`, 18 NBT, 11 LevelDB, 38 RakNet, 16 packet-generator, 29 protocol-import.
- Existing non-blocking analyzer warnings remain in `BlockCrackFanoutTests` (xUnit2013) and `DimensionIdAlignmentTests` (xUnit1031); neither was introduced by this initiative.
- Manual Bedrock smoke and scale benchmarks remain release gates when a deployed environment/real custom-container feature is in scope.

## Deliberately open work

1. Benchmark falling-block/viewer fan-out and inventory session activity at 10/100/500 players before changing tick work caps.
2. Implement phase 4 only for a roadmap-approved real container; do not add a test-only fake container or speculative registry.
3. Before Trade, decide runtime-only versus durable atomic completion and add its explicit multi-participant tests.
4. **Bootstrap DX:** `ZenithServer` currently constructs its production storage, palette, GUID and RakNet dependencies directly. GameLoop failure and actual RakNet drain are covered at their owning layers, but full server startup-failure/settle/flush ordering cannot be isolated without a deliberately designed composition seam. Add that seam only if an embedding scenario or a second lifecycle implementation requires it; do not add a generic host/factory solely to mock bootstrap.

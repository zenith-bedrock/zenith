# Zenith — Inventory & Container Architecture Plan

**Status:** phases 0–3 implemented and verified through `44304de` (2026-08-11); phase 4 is deliberately deferred until a real single-owner container is approved. This plan is not authorization to implement TradeInventory, a plugin API, or a generic inventory framework.

## Intended outcome

After phases 0–4, ordinary Bedrock item actions resolve through an authoritative open-container session to a domain slot without `InGameInventoryHandler`, packet DTOs, or a global flat-slot switch learning each custom container. Trade remains a later feature because it needs a multi-owner commit.

Preserve the architectural split: Packets serialize, Session interprets wire, Gameplay decides, Protocol transmits. Do not add `IInventory`, `IInventoryStrategy`, public registry, a system per container, scheduler, actor model, or plugin API.

## Order

| Phase | Deliverable | Priority | Depends on |
|---|---|---|---|
| 0 | Characterization and security baseline | P1 | — |
| 1 | Explicit current-container lifecycle | P1 | 0 |
| 2 | Resolve protocol slots to domain slots | P1 | 0, 1 |
| 3 | Session-scoped replication and freshness | P2 | 1, 2 |
| 4 | Single-owner custom-container fitness spike | P2 | 0–3 |
| 5 | Trade atomic-operation design/implementation | P1 when approved | 0–4 + durability decision |
| 6 | Scale/security regression gate | P2 | each implemented phase |

## Phase 0 — Characterization and security baseline

**Goal:** protect shipped behavior before moving identity/resolution seams, and correct the verified stale-chest authorization gap independently.

**In scope:** `src/zenith.Tests/InventoryRearrangeTests.cs`, `ChestLidTests.cs`, `ChestPairTests.cs`, `InventoryStackNetIdTests.cs`, `IntentContractTests.cs`; the smallest production change required for action-time spatial chest validation.

1. Add direct `InventorySystem` tests for player↔chest rollback, unopened/stale chest slots, matching close behavior, disconnect/death cleanup, and duplicate requests.
2. Capture `<=0` StackNetworkId client behavior in tests; do not silently tighten it.
3. Test that every spatial chest mutation revalidates target existence and reach; implement only that check. Future non-spatial sessions must use their own authorization predicate.

**Verify:** `dotnet test zenith.sln` exits 0. Manual smoke: open chest, leave reach, send an ISR action, and confirm error/resync with unchanged inventory/chest.

**Stop and report:** if real clients require the rejected operation, record the packet sequence and revise policy before changing production code.

## Phase 1 — Explicit current-container lifecycle

**Goal:** replace unrelated `OpenChest`, `InventoryWindowOpen`, and craft state as the authoritative opened target without generalizing inventory behavior.

**Expected files:** `Player/Player.cs`, `Player/InventoryWindowIntent.cs`, `Gameplay/Systems/InventorySystem.cs`, `Gameplay/ChestLidFanout.cs`, `Session/NetworkSession.cs`, and associated tests.

1. Add one internal `OpenContainerSession` owned by `Player`: protocol window id/type, monotonic generation, and typed current target. Initially targets are only shipped player UI and `OpenChestView`.
2. Route open, close, death, chest destruction, and disconnect through one session-close lifecycle. Incoming close must match the active protocol window.
3. Keep chest lid fan-out in World/chest behavior invoked by lifecycle; do not move it into Protocol or events.

**Verify:** existing chest lifecycle tests plus wrong-window, stale-close, and session-generation tests; `dotnet test zenith.sln`; manual double-chest/death/disconnect smoke.

**Stop and report:** if this requires unrelated BlockSystem/Protocol decisions, retain a compatibility projection temporarily and document the coupling rather than widening scope.

## Phase 2 — Resolve protocol slots to domain slots

**Goal:** eliminate semantic global flat ranges from Gameplay while retaining one Bedrock mapping adapter.

**Expected files:** `Protocol/InventoryContainerMap.cs`, `Player/InventoryStackIntent.cs`, a small type in `Player/` or `Gameplay/` consistent with dependency direction, `Player/InventorySlotResolver.cs`, `Gameplay/Systems/InventorySystem.cs`, `Protocol/InventoryProtocol.cs`, and tests.

1. Keep `InventoryContainerMap` as the only Bedrock id/slot decoder, but return a neutral `SlotReference`, not `ChestBase`/`CraftUiBase` semantics.
2. Resolve through the active session to `ResolvedSlot(container/view,index)`. Resolution only locates storage; it does not perform recipe, creative, permission, or trade decisions.
3. Convert generic transfer/swap/drop to resolved slots. Preserve explicit craft/creative action behavior and response coordinates.
4. Remove Gameplay dependence on flat sentinels only after current tests prove parity. Protocol may retain encoding constants.

**Verify:** map round trips; player/cursor/chest/double-chest/craft tests; full `dotnet test zenith.sln`. Add a guard that Gameplay imports no Packet DTOs or Bedrock container ids beyond a documented neutral boundary.

**Stop and report:** do not add a common `IInventory` merely to compile. If more than read/write/bounds/snapshot is required, identify the real separate concern first.

## Phase 3 — Session-scoped replication and freshness

**Goal:** remove `InventoryNetIds` dependence on fixed global ranges and make stale-session policy explicit.

**Expected files:** `Protocol/InventoryNetIds.cs`, `Protocol/InventoryProtocol.cs`, session/resolution types, handler validation, tests.

1. Scope advertised stack identity to active session/view plus domain slot, preserving the protocol cache in `Protocol`.
2. Define/test session-generation and bounded request-id/reconcile policy. Preserve only non-positive stack-id exceptions confirmed by Phase 0.
3. Send success/error content through active resolved session so future shared containers enumerate viewers without `SendTradeContent` branches.

**Verify:** stale session/replay and net-id remint tests; full `dotnet test zenith.sln`; manual ISR rearrange smoke.

**Stop and report:** if normal client moves use non-positive ids for non-empty sources, use session revision freshness rather than rejecting legitimate traffic.

## Phase 4 — Single-owner custom-container fitness spike

**Goal:** prove the seam with the least complex custom behavior before multi-player trade.

**Candidate:** server-owned withdraw-only mailbox only if accepted on the roadmap; otherwise a test-only in-memory container under `zenith.Tests`, not a product feature.

Register/open it through the session seam, give it domain access rules, and prove take/place rejection plus replication without changing packet decoding, handler action baking, or a central type switch.

**Verify:** no-network tests for open, authorize, transfer, reject, close, resync; `dotnet test zenith.sln`. Review diff: no concrete custom-container branch in handler or packet serializer.

## Phase 5 — Trade atomic operation (only when feature-approved)

**Goal:** implement an explicit two-party operation, not two independent normal ISR requests.

**Preconditions:** phases 0–4 complete; ADR decides runtime-only vs durable atomicity; concurrency/lifecycle policy is specified.

1. Add session-owned `TradeSession` and two participant views. Offer edits use normal session resolution and invalidate both confirmations.
2. Add a GameLoop-only `TryCommitTrade`: validate both sessions/confirmations/capacity, snapshot every participant, apply all moves, restore every participant on every failure, replicate both sessions.
3. Cancel on close/disconnect/death; reject stale generation/request actions.
4. If durable atomic completion is required, design storage multi-key/WAL support in a separate ADR/plan before acknowledging completion.

**Verify:** direct tests for success, full inventory, invalid offer, confirmation invalidation, replay, close/disconnect, and every partial-failure rollback; two-client smoke only after unit coverage.

## Phase 6 — Scale and security regression gate

Benchmark idle 10/100/500 online sessions, actions per tick, snapshot allocation, and viewer fan-out. Extend the existing inventory wire benchmark with session/container cases. Re-run `dotnet test zenith.sln` and alpha inventory/chest smoke gates. Reject O(all containers) tick work unless measured and justified.

## Review invariants

- New custom container does not alter `ItemStackRequestPacket` decoding unless Bedrock protocol itself requires it.
- `InGameInventoryHandler` remains wire-to-intent only; no `TradeInventory`/merchant/guild/mailbox rules.
- Protocol retains serialization and per-session wire identity; Gameplay does not reference packets or binary streams.
- Resolution, validation, container behavior, lifecycle, persistence, and replication remain separate responsibilities.
- A feature does not become an `IGameSystem` merely because it has inventory behavior.

# Zenith — Inventory & Container Extensibility Audit

**Scope:** source survey at `21b6ab2` (2026-08-11). This is audit/documentation only; it neither implements a refactor nor TradeInventory.

## Executive conclusion

Zenith has a sound authority boundary for the inventory types it ships: packets decode in `Session`, requests are bounded and queued, and mutations occur serially in `InventorySystem` on the GameLoop. `InventoryContainerMap` is a useful centralized protocol map.

It is **not yet a clean custom-container platform**. That map deliberately collapses wire `(container id, slot)` into a global meaningful `int flat`; `InventorySystem`, `InventorySlotResolver`, `InventoryProtocol`, and `InventoryNetIds` then interpret flat ranges as player/chest/crafting. The open target is three unrelated Player fields, not an authoritative container session. This supports player inventory, one chest view, and 2x2 craft UI, but not TradeInventory with two owners, participant views, and one atomic commit.

The minimum justified direction is not a generic `IInventory` framework: (1) characterization/security tests, (2) resolved domain slots instead of semantic flat integers in gameplay, (3) explicit open-container session, (4) runtime dispatch to a resolved target, then (5) a separately approved multi-owner operation for Trade.

## Method and external comparison

Read paths include `InventorySystem`, both inventory/use handlers, `InventoryContainerMap`, `InventorySlotResolver`, `InventoryProtocol`, `InventoryNetIds`, `PlayerInventory`, `PlayerCraftUi`, `ChestStore`, `ChestPairing`, packet DTOs, lifecycle code, and inventory tests. Search found no armor/offhand inventory implementation: it is unsupported, not a hidden mapping case.

External comparisons are architectural only: Mojang's [protocol overview](https://github.com/Mojang/bedrock-protocol-docs/blob/main/additional_docs/BlockBreakingOverview.md), PocketMine's [4.0 inventory changes](https://github.com/pmmp/PocketMine-MP/blob/stable/changelogs/4.0.md), PocketMine's [transaction rewrite](https://github.com/pmmp/PocketMine-MP/blob/stable/changelogs/3.0-alpha.md), and [Minestom](https://github.com/Minestom/Minestom). No claim relies on unverified plugin anecdotes.

## Current model

```text
Bedrock ItemStackRequestPacket                         network/session thread
  -> Packets decoder creates wire actions
  -> InGameInventoryHandler validates supported action shape, maps
     (containerId, slot) through InventoryContainerMap to an int flat,
     queues InventoryStackIntent (cap 8)
  -> Player FIFO                                        _inventoryStackLock
  -> InventorySystem.Tick                               GameLoop only
     drains all windows, then all stack requests in player order
     validates per-session advertised StackNetworkIds
     snapshots PlayerInventory + PlayerCraftUi + current OpenChestView
     applies transfer/swap/drop/craft; restores snapshots on failure
  -> InventoryProtocol sends ISR response/content; World persists success
```

| Stage | Owner / representation | State, allocation, ordering |
|---|---|---|
| Wire | `DecodedStackRequestAction`, `StackRequestSlotInfo`, `NetworkItemStack` | Decoder arrays per request; unsupported actions rejected before queue. |
| Wire map | `InventoryContainerMap.TryMap()` | One protocol-only switch; it loses target identity by emitting flat integers. |
| Pending state | `InventoryStackIntent` / `InventoryWindowIntent` on `Player` | FIFO locks bridge network producer and GameLoop consumer; caps are 8 and 4. |
| Authority | `InventorySystem.Tick()` | Windows for all players before ISR for all players. Serialized mutation, not periodic simulation. |
| State | Player inventory, craft UI, `ChestStore` | Player-owned, ephemeral player-owned, world-owned. |
| Resolution | `InventorySlotResolver` | Reads/writes live Player/World state by flat range; GameLoop-only mutation. |
| Rollback | snapshots in `InventorySystem.cs:136-141` | Allocates 36+cursor, 5 craft, and 27/54 chest arrays plus wire-touch list. |
| Replication | `InventoryProtocol` / `InventoryNetIds` | Per-session stack IDs; response then relevant full content. |

### Slot resolution

`Protocol/InventoryContainerMap.cs:61-125` is the SSOT for Bedrock ids 12, 13, 14, 28, 29, 59, 60, and 7. It maps them to player `0..35`, cursor `-1`, chest `100..153`, and craft UI `200..204`; inverse mapping builds responses. This is an **acceptable protocol mapping** today.

`Player/InventorySlotResolver.cs:18-51` is the actual domain-location resolver. It knows chest, craft grid/result, then defaults to player inventory. Its name and focused read/write role are good, but its input is protocol-derived and its storage choices are concrete. It cannot resolve a second player, trade offer, server mailbox, or shared bank.

| Target | Current owner | Status |
|---|---|---|
| Hotbar, bag, cursor | `PlayerInventory` | Supported; cursor is ephemeral/player-local. |
| Chest/double chest | coordinate-keyed `ChestStore`, `OpenChestView` | Supported; 54-slot view projects two 27-slot persistent cells. |
| Crafting | `PlayerCraftUi`, `RecipeRegistry` | Supported only for player 2x2 with dedicated action kinds. |
| Creative | `CreativeCatalog`, `CraftCreative` branch | Supported as a dedicated rule. |
| Armor/offhand | none | Unsupported. |
| Custom/temporary | none | Unsupported. |

### Container identity and lifecycle

`Player.cs:162-168` uses `OpenChestView? OpenChest`, `bool InventoryWindowOpen`, and always-present `CraftUi`. `InventorySystem.cs:104-112` recognizes only wire window 0 and otherwise releases whichever chest happens to be open; close id/type do not identify an authoritative domain target. `OpenChestView` is coordinate-based and capacity-one per Player. Chest opener sets only drive lid animation; they are not a session, ACL, or viewer registry.

Opening checks block interaction in `InGameUseItemHandler.cs:65-87`, then stores the view. Subsequent ISR mutations check only `OpenChest` and `SlotCount` (`InventorySystem.cs:370-377`), not reach or live chest existence. Block destruction closes views; death/disconnect clear/release state (`Player.cs:536-550`, `NetworkSession.cs:174-182`).

## Transaction model

One `InventoryStackIntent` is the transaction boundary. It atomically covers the actor's `PlayerInventory`, `PlayerCraftUi`, and the chest visible through their `OpenChestView`; a double chest is included because its 54 visible slots are captured. Validation checks advertised stack IDs, source/count/capacity, and open-view bounds. Failure restores all captured in-memory state and resyncs. `TryDrop` deposits before mutating the source, so its failed deposit does not lose an item.

It does not include another Player, arbitrary inventory, more than one open target, or persistence. `PersistInventory` and `PersistChest` happen after success response in independent writes (`InventorySystem.cs:263-274`). Thus runtime mutation is all-or-nothing but durability is not a multi-key transaction. GameLoop serialization prevents simultaneous state writes, but it does not make two independently queued player requests atomic.

## Findings

### ZIA-001 — Introduce an authoritative active-container session

- **Severity:** P1; **Confidence:** High; **Area:** identity/extensibility.
- **Evidence:** `Player.cs:162-168`; `InventorySystem.cs:63-112`; `InventorySlotResolver.cs:18-51`.
- **Current impact:** shipped chest/craft behavior works.
- **Future extensibility impact:** Trade, merchant, guild bank, mailbox, and per-view ACLs require concrete fields/cases across handler, map, resolver, system, protocol cache, and lifecycle.
- **Recommendation:** one internal `OpenContainerSession` per Player, binding protocol window assignment/generation to a domain target/view. It owns lifecycle, not transaction rules.

### ZIA-002 — Stop using semantic flat ranges past the protocol boundary

- **Severity:** P1; **Confidence:** High; **Area:** protocol/domain separation.
- **Evidence:** `InventoryContainerMap.cs:25-39`; `InventorySystem.cs:370-377`; `InventorySlotResolver.cs:18-51`; `InventoryNetIds.cs:24-43`.
- **Current impact:** centralized wire switch is correct for current containers.
- **Future extensibility impact:** each target needs a flat range plus synchronized concrete branches.
- **Recommendation:** decode `SlotReference(containerId, slot)` in Protocol, resolve it through the active session to `ResolvedSlot(container/view, index)` before gameplay. Resolution is location only; validation/rules remain elsewhere.

### ZIA-003 — Revalidate active chest authority at mutation time

- **Severity:** P1; **Confidence:** High; **Area:** authorization/stale session.
- **Evidence:** opening at `InGameUseItemHandler.cs:65-87`; mutation validity only at `InventorySystem.cs:370-377`.
- **Current impact:** player can move away after opening and still mutate until close/destruction/disconnect. They cannot select an arbitrary unopened chest, so this is not current cross-container injection.
- **Future extensibility impact:** stale unauthorized access becomes easy to reproduce in shared/session containers.
- **Recommendation:** session validation checks target existence, authorization, and reach for spatial targets on every mutation. Non-spatial containers use an explicit authorization predicate, never fake coordinates.

### ZIA-004 — Define ISR freshness before multi-party state depends on it

- **Severity:** P2; **Confidence:** High; **Area:** replay/anti-desync.
- **Evidence:** `InventoryProtocol.cs:145-151` accepts all `<=0` ids; `InventoryStackNetIdTests.cs:35-43` asserts this; no request-id epoch/deduplication is stored.
- **Current impact:** live source/count/capacity checks prevent obvious duplicate transfer after replay, but this is not a full freshness guarantee.
- **Future extensibility impact:** confirmations and one-shot virtual outputs need stronger idempotency than soft slot matching.
- **Recommendation:** characterize real-client traffic first, then give sessions a generation and bounded request-id/revision policy.

### ZIA-005 — Treat persistence as a distinct durability boundary

- **Severity:** P2; **Confidence:** High; **Area:** durable atomicity.
- **Evidence:** separate post-response writes at `InventorySystem.cs:263-274`.
- **Current impact:** runtime rollback is solid; a crash can still lose recent durable state under the documented crash-soft model.
- **Future extensibility impact:** Trade cannot promise durable atomic completion until storage exposes a multi-key commit boundary.
- **Recommendation:** decide through ADR whether initial Trade is runtime-atomic only; add storage transaction support separately if durable atomicity is required.

### ZIA-006 — Do not turn the slot resolver into an inventory strategy god object

- **Severity:** P2; **Confidence:** High; **Area:** behavior boundary.
- **Evidence:** explicit recipe, created-output, and creative rules at `InventorySystem.cs:150-201`.
- **Current impact:** concrete exceptional behavior is clear and functional.
- **Future extensibility impact:** moving it into one strategy/resolver would conflate resolution, authorization, crafting, trade confirmation, and lifecycle.
- **Recommendation:** resolver answers where; container operation/rule methods answer whether; recipe/creative/trade remain explicit operations.

## Smells: classification

| Occurrence | Classification | Verdict |
|---|---|---|
| `InventoryContainerMap.TryMap` switch | Protocol mapping — acceptable | Retain as Bedrock adapter; stop emitting semantic global flats. |
| `InventorySystem.ApplyWindow` OpenChest switch | Domain hardcoding — investigate | Fine for one block container, becomes central extension point. |
| `InventorySlotResolver` chest/craft branches | Domain hardcoding — investigate | Focused seam, but flat namespace couples every type. |
| `InventoryNetIds` fixed arrays/ranges | Cross-layer knowledge — problematic | Assumes only player/chest/craft spaces. |
| `InventoryProtocol.SendChest*` | Protocol mapping — acceptable today | Future generic send must be session/view-driven, not Trade-specific. |
| `ChestStore` coordinate storage/lid bookkeeping | Domain-specific — acceptable | Belongs in World; do not make every container a block entity. |

## Fitness tests and impact

| Trade change today | Classification | Rationale |
|---|---|---|
| new `TradeSession`, offers, confirmations | Expected | New session-owned domain behavior. |
| Player fields and cleanup | Suspicious | Missing generic open-session lifecycle. |
| `InventorySystem` action/window/snapshot branches | Architecture leak | Core learns Trade behavior and participants. |
| `InventoryContainerMap` new range/id | Architecture leak | Protocol map becomes gameplay container catalog. |
| `InventorySlotResolver` / `InventoryNetIds` trade branch | Architecture leak | Domain storage leaks into resolver/protocol cache. |
| `InventoryProtocol` Trade-specific send | Suspicious | Wire mapping may be required; concrete Trade knowledge is not. |
| `InGameInventoryHandler` | Suspicious unless new Bedrock action/handshake | Generic Take/Place/Swap should remain generic. |
| packet decoder / serializer | Highly suspicious | Change only for actual protocol requirement. |

| Feature | Handler changes | Core transaction changes | Registration only | New domain code | Architectural issue today? |
|---|---:|---:|---:|---:|---|
| TradeInventory | yes | yes | no | yes | session, flat slots, multi-owner atomicity |
| Merchant | yes | yes | no | yes | server-owned/read-only view |
| GuildBank | yes | yes | no | yes | shared persistence, ACL, viewers |
| Mailbox | yes | yes | no | yes | server-owned withdraw-only view |
| Custom equipment | yes | yes | no | yes | new wire mapping; no armor baseline |
| Custom craft station | yes | yes | no | yes | custom input/output rules |

After the minimum refactor, all need new domain code and composition registration. Handler/packet changes are necessary only for different Bedrock wire behavior; Trade alone additionally needs its explicit multi-owner commit.

## Decision matrix

| Concern | Current approach | Works today? | Extensible? | Recommended |
|---|---|---:|---:|---|
| Container identity | three Player fields / coordinate view | Yes | No | typed open session |
| Slot resolution | wire -> global flat -> concrete branch | Yes | Partly | wire reference -> session-resolved slot |
| Transaction processing | one system action switch | Yes | Partly | retain runtime; dispatch resolved operation |
| Validation | stack ids + generic/action rules | Yes | Partly | session access separate from business rules |
| Rollback | player/craft/current chest snapshot | Yes | No multi-owner | later explicit participant snapshot set |
| Custom containers | none | N/A | No | composition registration after seam |
| Handler coupling | wire baking but semantic-flat output | Yes | Partly | handler stays wire-only |
| Protocol/domain | good folders, leaking flat namespace | Partly | No | domain identity after adapter |
| Tick processing | drains queues each tick | Yes | Yes | retain serialized authority; no system per container |

## Target architecture

```text
CURRENT: wire (container,slot) -> map -> int flat -> system range checks -> concrete storage

TARGET:  wire (container,slot) -> SlotReference        [Protocol]
          -> OpenContainerSession.Resolve(reference)    [authoritative Player session]
          -> ResolvedSlot(container/view,index)          [domain location]
          -> InventorySystem generic move/swap/drop
          -> container view validates access/mutation rule
```

`OpenContainerSession` is a concrete lifecycle value: protocol window assignment, generation, target/view, close/disconnect cleanup. Initially it wraps only existing player UI and `OpenChestView`. A registry is justified only after a second custom view exists; it selects openers/resolvers, never transaction rules.

```text
Normal: Packet -> SlotReference -> player session -> ResolvedSlot -> generic move -> response
Chest:  Packet -> SlotReference -> chest session/view -> reach/access -> generic move -> replication
Trade:  Packet -> SlotReference -> trade participant view -> offer rule -> explicit TryCommitTrade
        (validate/snapshot/apply-or-restore all participants) -> replicate both
Custom: Packet -> SlotReference -> registered session/view -> domain rule -> generic mutation
```

No `IInventoryStrategy`, generic engine, plugin API, or `IGameSystem` per container is recommended. These are different behaviors on the same serialized runtime unless a feature introduces independently ticking simulation.

## Scale, testability, and characterization

Idle work is O(players) twice per tick; queued work is O(requests + actions). Transfer/swap is O(1); a request snapshots O(36 + 5 + 27/54). Full response serialization costs affected slots; shared containers add explicit O(viewers) replication. At 10/100/500 players this is more a structural measurement concern than a current micro-optimization target. Reject future O(all containers) tick work unless benchmarked.

Strong no-network seams already exist: `PlayerInventory`, `ChestStore`, mapping, net ids, and `InventorySystem` tests use `IntentTestFixture`. A Trade commit must be directly unit-testable without RakNet, packets, or handler construction.

Before any refactor, add characterization coverage for player/cursor transfer, chest open/close and two viewers, chest rollback/double half, craft/created-output, creative, invalid wire slot/count/stack-id, full inventory, stale/closed window, action-time reach/target existence, duplicate request behavior, disconnect/death cleanup, and failed floor-drop rollback.

## Comparison conclusion

Mojang documents wire traffic, not a domain container model; Zenith's Packet/Session/Gameplay split is appropriate. PocketMine's public changelogs show transaction-action modeling and an eventual split between temporary positioned gateways and persistent storage. They are a warning not to conflate client windows with ownership/lifecycle, not a C# design to copy. Minestom explicitly opens domain inventories rather than assuming a chest block owns a UI; that supports a session/view seam, while its Java/API-first model does not answer Bedrock ISR mechanics. No unverified current-internal claims are made for Nukkit, Cuberite, Pumpkin, Dragonfly, or Endstone.

PocketMine's public history verifies removal of `CustomInventory` in its 4.0 transition and addition of temporary inventories; it does not prove the reported reflection/hack story. The useful transferable lesson is explicit target/lifecycle identity rather than hardcoded window/slot classification.

## Required answers

**Can someone implement TradeInventory in six months without knowing Zenith protocol internals? — NO.** Today they must understand map ranges, net-id cache, chest sends, Player fields, and transaction snapshots/action branches. The smallest required refactor is active-container session plus domain-slot resolution; multi-owner atomic commit is a distinct Trade prerequisite.

**Are we building an extensible inventory system or one correct for known inventories? — Today, the latter, with promising foundations.** The strict wire/authority boundary and centralized map are worth preserving. The staged seam below avoids both the hardcoded treadmill and a premature framework of interchangeable interfaces.

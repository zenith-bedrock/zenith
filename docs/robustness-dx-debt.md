# Robustness / DX debt

Platform-health work tracked separately from Horizon‑1 product ([`roadmap.md`](roadmap.md)). Recorded so later PRs cite one artifact instead of rediscovering the diagnostic.

**ADR:** [`decisions.md`](decisions.md) §54.

## Three baskets

| Basket | What | Action |
|--------|------|--------|
| **A** | H1 product gaps (gravity, players/, world beyond flat) | Stay in [`roadmap.md`](roadmap.md) — **out of scope** for this program |
| **B** | Dig/UI mutation on RakNet thread; dual Online fan-out; fat `InGameSessionHandler`; chat overwrite-latest; mega `InventoryProtocol`; GC on send/tick | Prioritize in Phases 1–5 |
| **C** | Stale ARCHITECTURE Fase 3, missing `.cursor/rules` stubs, dead `ChunkUtils` / usings | Cheap cleanup (Phase 4) |

Spine (decide → transmit → serialize) is healthy. Limits are cross-thread mutation, handler size, and allocs on hot paths — not missing ECS/plugins.

## Priority tables

### P0 — perf

| Item | Symptom | Phase | Status |
|------|---------|-------|--------|
| Shared Online snapshot | ~7 `ToArray` / tick; nested peer loops re-snapshot | 1 | **Closed (jul 2026):** GameLoop `FillOnline` + systems use tick `online`; test overloads use `_onlineScratch`; Session helpers use `SnapshotOnline()` |
| Send-path copies | `Encode().ToArray()` + UDP `Span.ToArray()` | 1 | **Mostly closed:** `GamePacket.EncodeOwned()` / `SendDataPacket` already avoid double Encode+ToArray. Remaining allocs (payload inner `Encode`, UDP) are Phase 3 unless benchmarks prove hot |
| Overlay column list | `GetOverlaysInColumn` → `new List` per column send | 3 | Open — prefer `FillOverlaysInColumn` callers |
| AuthInput bitset | `List` + `ToArray` per packet | 3 | Open |
| `WriteVarString` | UTF-8 alloc per string write | 3 | Open |
| FloorDrop delay keys | key-list alloc on tick | 3 | Open |

### P0 — arch

| Item | Symptom | Phase | Status |
|------|---------|-------|--------|
| Dig / crack / dig-swing on tick | `BeginBreak` / crack fan-out on RakNet thread | 2 | **Closed:** `SubmitDig*` → BlockSystem |
| Chest / inventory window on tick | `OpenChest` / `InventoryWindowOpen` mutated off-tick | 2 | **Closed:** window intents |
| Peer FX rule | Crack/swing/Absolute FLAGS/equipment/chat ownership | 2 | Mostly closed; Session helpers for join/skin/emote remain |
| Handler channels | God-file `InGameSessionHandler` | 2 | **Partial:** `partial` files — still one switch |

### P1 / P2

| Item | Phase | Status |
|------|-------|--------|
| Chat FIFO (cap like block edits) | 4 | Open |
| ARCHITECTURE / CONTRIBUTING honesty | 4 | **Partial:** ARCHITECTURE double-chest gap synced (jul 2026) |
| Dead `ChunkUtils`, unused usings | 4 | Open |
| `InventoryProtocol` builders + façade | 5 | **Closed** (§54 Phase 5) |

## Suggested order

```text
Phase 0  debt doc + ADR §54          done
Phase 1  Online-once + EncodeOwned   done (jul 2026 audit follow-up)
Phase 2  dig/UI on tick + partial    mostly done
Phase 3  zero-alloc hot pack         next measured leaf
Phase 4  DX + P2 + chat FIFO
Phase 5  InventoryProtocol split     done
```

One leaf ≈ one ADR adendo (or §54 sub-leaf) + one PR. Freeze list unchanged.

## Non-goals (entire program)

Plugin API, DI, VisibilitySystem, ECS, Scheduler, FormSystem, inventory-options product, mass C# cosmetics (primary-ctor / switch rewrites), PM/BDS parity chase, `Network/` revival.

## Explicit defer — `World/` → `Item/` folder cut

Do **not** open a cosmetic `Item/` PR in this program. Trigger only when Horizon‑1 registry/dimension work forces a clean boundary; record that trigger in a new ADR adendo then.

## EventBus

Leave inert (no `Subscribe` product surface). Login/quit hooks that already exist stay; do not grow a bus API here.

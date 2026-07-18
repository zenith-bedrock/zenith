# Robustness / DX debt

Platform-health work tracked separately from Horizon‑1 product ([`roadmap.md`](roadmap.md)). Recorded so later PRs cite one artifact instead of rediscovering the diagnostic.

**ADR:** [`decisions.md`](decisions.md) §54.

## Three baskets

| Basket | What | Action |
|--------|------|--------|
| **A** | H1 product gaps (tools, double-chest, gravity, persist, world beyond flat) | Stay in [`roadmap.md`](roadmap.md) — **out of scope** for this program |
| **B** | Dig/UI mutation on RakNet thread; dual Online fan-out; fat `InGameSessionHandler`; chat overwrite-latest; mega `InventoryProtocol`; GC on send/tick | Prioritize in Phases 1–5 |
| **C** | Stale ARCHITECTURE Fase 3, missing `.cursor/rules` stubs, dead `ChunkUtils` / usings, CONTRIBUTING silence on Online-once | Cheap cleanup (Phase 4) |

Spine (decide → transmit → serialize) is healthy. Limits are cross-thread mutation, handler size, and allocs on hot paths — not missing ECS/plugins.

## Priority tables

### P0 — perf

| Item | Symptom | Phase |
|------|---------|-------|
| Shared Online snapshot | ~7 `ToArray` / tick; nested peer loops re-snapshot | 1 |
| Send-path copies | `Encode().ToArray()` + UDP `Span.ToArray()` | 1 |
| Overlay column list | `GetOverlaysInColumn` → `new List` per column send | 3 |
| AuthInput bitset | `List` + `ToArray` per packet | 3 |
| `WriteVarString` | UTF-8 alloc per string write | 3 |
| FloorDrop delay keys | key-list alloc on tick | 3 |

### P0 — arch

| Item | Symptom | Phase |
|------|---------|-------|
| Dig / crack / dig-swing on tick | `BeginBreak` / crack fan-out on RakNet thread | 2 |
| Chest / inventory window on tick | `OpenChest` / `InventoryWindowOpen` mutated off-tick | 2 |
| Peer FX rule | Crack/swing/Absolute FLAGS/equipment/chat ownership unclear vs Session helpers | 2 |
| Handler channels | God-file `InGameSessionHandler` — split as `partial` only | 2 |

### P1 / P2

| Item | Phase |
|------|-------|
| Chat FIFO (cap like block edits) | 4 |
| ARCHITECTURE / CONTRIBUTING / `.cursor/rules` honesty | 4 |
| Dead `ChunkUtils`, unused usings | 4 |
| `InventoryProtocol` builders + façade | 5 |

## Suggested order

```text
Phase 0  debt doc + ADR §54          (this file)
Phase 1  Online-once + send copies
Phase 2  dig/UI on tick + peer FX + partial handler
Phase 3  zero-alloc hot pack
Phase 4  DX + P2 + chat FIFO
Phase 5  InventoryProtocol split
```

One leaf ≈ one ADR adendo (or §54 sub-leaf) + one PR. Freeze list unchanged.

## Non-goals (entire program)

Plugin API, DI, VisibilitySystem, ECS, Scheduler, FormSystem, inventory-options product, mass C# cosmetics (primary-ctor / switch rewrites), PM/BDS parity chase, `Network/` revival.

## Explicit defer — `World/` → `Item/` folder cut

Do **not** open a cosmetic `Item/` PR in this program. Trigger only when Horizon‑1 registry/dimension work forces a clean boundary; record that trigger in a new ADR adendo then.

## EventBus

Leave inert (no `Subscribe` product surface). Login/quit hooks that already exist stay; do not grow a bus API here.

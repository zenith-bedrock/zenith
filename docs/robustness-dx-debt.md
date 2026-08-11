# Robustness / DX debt

Platform-health work tracked separately from Horizon‑1 product ([`roadmap.md`](roadmap.md)). Recorded so later PRs cite one artifact instead of rediscovering the diagnostic.

**ADR:** [`decisions.md`](decisions.md) §54.

## Critical delivery risks (audit refresh — jul 2026)

These are **harder** than the soft LAN-alpha grade. Do not sand them down in the next write-up.

| Risk | Why it hurts | Stance |
|------|----------------|--------|
| **Dirty tree better than `origin`** | Product truth lived only on one machine; remote lied (Online-once semi-fake, stale docs). Anti-pattern of process, not “WIP”. | **Rule:** hygiene that closes an audit/ADR gap lands as **commit + push the same day**. Dirty &gt; remote = process failure. |
| **Overlay / store grief** | Warn-only unbounded `_blockOverrides` / chests = RAM+disk DoS under place/dig grief. SoftCap deferred “because dig→air” was too kind to attackers. | **Shipped (jul 2026):** overlay SoftCap + compact-to-base; chest SoftCap on new cells (§36 adendo). Eviction/compaction redesign still open for true production. |
| **No Bedrock E2E in CI** | ~leaf tests without automated client smoke puts the entire join/place/chest/dig gate on one human. Bus factor × missing E2E = beta blocker. | **Shipped:** leaf `dotnet test` on every push/PR to `develop`/`main` (`.github/workflows/ci.yml`). **Still open (beta-hard):** automated Bedrock client / protocol harness — tag gates stay human smoke until that exists. Spike: [`zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) (ADR §58). |

**Bus factor** remains high (dominant author). Documentation and optional automation checks help contributors, but do **not** replace peer review of dig/ISR or Bedrock smoke.

**Feature pressure:** skipping ADR → smoke → tag while chasing gravity/`players/` recreates “declare done before the last 20%.”

## Three baskets

| Basket | What | Action |
|--------|------|--------|
| **A** | H1 product gaps (closed on `develop`) | Historical — see [`roadmap.md`](roadmap.md) for current gates; **out of scope** for this platform-health program |
| **B** | Dig/UI mutation on RakNet thread; dual Online fan-out; fat `InGameSessionHandler`; chat overwrite-latest; mega `InventoryProtocol`; GC on send/tick | Phases 1–5 mostly done; leftover scratch-list nits below |
| **C** | Stale ARCHITECTURE Fase 3, missing `.cursor/rules` stubs, dead `ChunkUtils` / usings | Mostly closed (jul 2026) |

Spine (decide → transmit → serialize) is healthy. Limits are cross-thread mutation, handler size, and allocs on hot paths — not missing ECS/plugins. Delivery discipline and proof (CI + smoke) are now first-class risks alongside basket B.

## Priority tables

### P0 — perf

| Item | Symptom | Phase | Status |
|------|---------|-------|--------|
| Shared Online snapshot | ~7 `ToArray` / tick; nested peer loops re-snapshot | 1 | **Closed (jul 2026):** GameLoop `FillOnline` + systems use tick `online`; test overloads use `_onlineScratch`; Session helpers use `SnapshotOnline()` |
| Send-path copies | `Encode().ToArray()` + UDP `Span.ToArray()` | 1 | **Mostly closed:** `GamePacket.EncodeOwned()` / `SendDataPacket` already avoid double Encode+ToArray. Remaining allocs (payload inner `Encode`, UDP) — reopen only if benchmarks prove hot |
| Overlay column list | `GetOverlaysInColumn` → `new List` per column send | 3 | **Closed (jul 2026):** `FillOverlaysInColumn` / `ForEachOverlayInColumn`; `ColumnSend` reuses overlay scratch ([`docs/dx.md`](dx.md) Phase 3 note). Allocating `GetOverlaysInColumn` remains for PreSpawn `ColumnReadResult` / tests (async join, not GameLoop) |
| AuthInput bitset | `List` + `ToArray` per packet | 3 | **Closed (jul 2026):** decode into `stackalloc` |
| `WriteVarString` | UTF-8 alloc per string write | 3 | **Closed (jul 2026):** `stackalloc` / `ArrayPool` in `BinaryStream` |
| FloorDrop delay keys | key-list alloc on tick | 3 | **Closed (jul 2026):** `FloorDropStore._delayScratch` reused |
| Tick/join UpdateBlock batch lists | `new List` for place/break fan-out + overlay catch-up | 3b | **Closed (jul 2026):** `BlockSystem` field scratches; `ColumnSend` ThreadStatic updates scratch |

### P0 — arch

| Item | Symptom | Phase | Status |
|------|---------|-------|--------|
| Dig / crack / dig-swing on tick | `BeginBreak` / crack fan-out on RakNet thread | 2 | **Closed:** `SubmitDig*` → BlockSystem |
| Chest / inventory window on tick | `OpenChest` / `InventoryWindowOpen` mutated off-tick | 2 | **Closed:** window intents |
| Peer FX rule | Crack/swing/Absolute FLAGS/equipment/chat ownership | 2 | Mostly closed; Session helpers for join/skin/emote remain |
| Handler channels | God-file `InGameSessionHandler` | 2 | **Partial:** AuthInput / Inventory / UseItem partials — dispatcher switch remains (~293 lines); good enough until a new channel forces another split |

### P1 / P2

| Item | Phase | Status |
|------|-------|--------|
| Chat FIFO (cap like block edits) | 4 | **Closed (jul 2026):** `Player.MaxPendingChat` + drain-all in `ChatSystem` (ADR §15 adendo) |
| ARCHITECTURE / CONTRIBUTING honesty | 4 | **Partial:** double-chest / H1 smoke rows synced; keep honest when cutting tags |
| Dead `ChunkUtils`, unused usings | 4 | **Closed:** `ChunkUtils.cs` removed |
| `InventoryProtocol` builders + façade | 5 | **Closed** (§54 Phase 5) |
| Leaf CI on push/PR | — | **Closed (jul 2026):** `.github/workflows/ci.yml` |
| Bedrock E2E CI | — | **Open — beta-hard**. Spike repo: [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) (Bun + bedrock-protocol, ADR §58) — opt-in; not Zenith PR-blocking |
| AuthInput rate × client FPS | Client may emit AuthInput (and related) more often at higher FPS; inbound still decoded every time. Outbound Absolute already dirty-gated (§43). | **Open — measure:** log/bench AuthInput Hz vs FPS under LAN load; only then coalesce/drop earlier (no ECS). Related: unused RakNet payload discard is not a separate leaf — movement already filters “no real move” before fan-out. |

## Suggested order

```text
Phase 0  debt doc + ADR §54          done
Phase 1  Online-once + EncodeOwned   done
Phase 2  dig/UI on tick + partial    mostly done
Phase 3  zero-alloc hot pack         done (jul 2026; see docs/dx.md)
Phase 3b tick/join UpdateBlock lists done (BlockSystem + ColumnSend scratches)
Phase 4  DX + chat FIFO              done (chat FIFO); ARCHITECTURE stay-honest ongoing
Phase 5  InventoryProtocol split     done
Next     roadmap Phase C actor characterization; then a materially different second actor and actor-pressure evidence. §61 Mojang remains
         opt-in only when import is needed; AuthInput×FPS remains measure-only.
         SoftCap eviction redesign; Bedrock E2E CI (beta-hard) still open.

```

One leaf ≈ one ADR adendo (or §54 sub-leaf) + one PR. Freeze list unchanged.

## Non-goals (entire program)

Plugin API, DI, VisibilitySystem, ECS, Scheduler, FormSystem, inventory-options product, mass C# cosmetics (primary-ctor / switch rewrites), PM/BDS parity chase, `Network/` revival.

## Explicit defer — `World/` → `Item/` folder cut

Do **not** open a cosmetic `Item/` PR in this program. Trigger only when Horizon‑1 registry/dimension work forces a clean boundary; record that trigger in a new ADR adendo then.

## EventBus

Leave inert (no `Subscribe` product surface). Login/quit hooks that already exist stay; do not grow a bus API here.

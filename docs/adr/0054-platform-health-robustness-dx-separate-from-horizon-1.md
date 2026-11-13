# ADR §54 — Platform health (Robustness / DX) — separate from Horizon‑1 product

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §54`.

**Choice:**

1. Platform-health leaves (Online-once, dig/UI on tick, zero-alloc hot pack, DX cleanup, InventoryProtocol split) are tracked in [`robustness-dx-debt.md`](../robustness-dx-debt.md), **not** as Horizon‑1 product rows.
2. Three baskets: **A** = roadmap product (tools, double-chest, …); **B** = cross-thread / fan-out / GC / fat handler; **C** = docs/dead code. Phases address B then C; A stays in [`roadmap.md`](../roadmap.md).
3. Freeze list unchanged at this historical milestone: no Scheduler, Actor/ECS, VisibilitySystem, DI, plugin API, `/` command framework, `Network/` revival. The command-framework portion is superseded by §100; the remaining freezes stand.
4. C# modernization only on measured hot paths (`Span` / `ArrayPool` / reused lists) — no mass primary-ctor / switch rewrite.
5. One leaf ≈ one ADR adendo (or §54 sub-leaf) + one PR.

**Why:** Spine is healthy; rediscovering “why not ECS” every PR wastes context. Debt doc is the cite target.

**Adendo — Peer FX rule (Phase 2):** Peer-visible FX (crack, swing, Absolute FLAGS, equipment, chat) apply on GameLoop tick. Documented exceptions: Session helpers for join/skin/emote (already §49/§53). Crack + dig swing move to tick with dig intents. §53 “immediate” dig/place swing is updated: dig start/abort swing fans on tick; place/attack/missed-swing paths keep tick or Session helper as already specified.

**Adendo — SelectedHotbarSlot:** Handler may write `SelectedHotbarSlot` as overwrite-latest input (like movement). EquipmentSystem fingerprints held on tick — no locks on Player fields.

**Adendo — InventoryProtocol split (Phase 5):** `InventoryContainerMap` (wire map SSOT), `CreativeContentBuilder`, `CraftingDataBuilder`, `InventoryNetIds` are Protocol-adjacent files; `InventoryProtocol` remains the transmit façade. No new layer names / Mapper framework. `World/` → `Item/` folder cut stays deferred (debt doc).

**Adendo — stack net id wire contract:** Callers use `DescribeForWire(flat, slot)` (refresh + DTO) — never ad-hoc Refresh+Get. `MatchesAdvertisedStackNetId(flat, clientId)` soft-checks ISR: `clientId ≤ 0` accept; positive must equal last advertisement; mismatch → Error + full inventory/UI/(chest) resync before mutate. Domain `InventorySlot` still has no id (DF-style deferred). Negative prediction ids deferred (§35).

**Adendo (jul 2026 — Online-once closed):** GameLoop `FillOnline` once/tick; systems take `online` and must not call `PlayerManager.Online` on the tick path (`ApplyWindow`, void-death, etc.). Test overloads use a per-system `_onlineScratch` + `FillOnline`. Session helpers use `SnapshotOnline()`. `GamePacket.EncodeOwned` already avoids Encode+ToArray double copy. Gameplay must not `using Zenith.Packets` — Respawn states via `EntityProtocol.SendRespawnSearching` / `SendRespawnReady`.

**Deferred:** `World/` → `Item/` folder cut until H1 registry/dimension forces it (see debt doc). Domain-owned stack net ids / NBT identity.

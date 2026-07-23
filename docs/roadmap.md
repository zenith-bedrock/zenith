# Roadmap (lean)

**Purpose:** shared order of work for humans and agents. **Not** PocketMine feature parity and **not** a second copy of every Deferred line in [`decisions.md`](decisions.md).

Update this file when a horizon closes (e.g. alpha tagged) or an ADR changes “yes-next”. Day-to-day constraints stay in [`ARCHITECTURE.md`](../ARCHITECTURE.md).

---

## Horizon 0 — Closed: `v0.0.1-alpha`

Tagged **v0.0.1-alpha** (Jul 2026). `main` and `develop` both at the release tip. Notes: [`release-notes-template.md`](release-notes-template.md) / [GitHub Release](https://github.com/zenith-bedrock/zenith/releases/tag/v0.0.1-alpha). Gate: [`alpha-gate.md`](alpha-gate.md).

**Already shipped on that tag:** LAN spine (login → flat InMemory **or** LevelDB world → place/break → inventory/chests/craft → Creative/Survival → persist → dig crack + void rescue → graceful disconnect). Chest facing (§46), MOTD/session hygiene (§47), ordered fragment reassembly (raknet), folder layout (§48).

**Do not slide into post-tag polish as Horizon 0:** sand/gravel gravity, drop-entity wire, death/respawn, tools, double-chest, `/` commands, plugins, biomes — those are Horizon 1+ or explicit non-goals.

---

## Horizon 1 — Honesty gaps — **closed** (Zenith leaf)

Do these **as separate ADRs + PRs**. Earlier items unblock later ones. **All rows below are shipped on `develop`** (Jul 2026). Mojang import (§61) is **not** part of this horizon’s Zenith-native leaf — see [Yes-next](#yes-next-after-h1).

| Order | Leaf | Notes |
|------:|------|--------|
| 1 | **Drop-entity wire** (minimal) | **Shipped:** `AddItemActor` / `TakeItemActor`; FloorDropStore holds entity id (§26 adendo). No ECS/physics/despawn. |
| 2 | **Death / Respawn** | **Shipped:** void → DeathInfo + Respawn handshake (§40 adendo); inventory kept. Soft-rescue retired. No damage pipeline / death drops. |
| 2b | **Block/item registry honesty** | **Shipped:** palette reverse `runtimeId→name` + `Blocks.IsPlaceable` allowlist (§12 adendo). |
| 3 | **`/gamemode` minimal** | **Shipped:** CommandRequest/chat → intent → `GameModeSystem` + remint CreativeContent (§52). Not a command framework. |
| 4 | **Tool dig speed / efficiency** | **Shipped:** DF `BreakDuration` + curated tools + 3602 on speed delta (§27 adendo). No enchants. |
| 5 | **Double-chest** (sneak-place + 54 UI) | **Shipped** in `v0.0.2-alpha` (ADR §56) — pair + 54 UI + 2×`ct:`. |
| 6 | **Block gravity** (sand/gravel) | **Shipped:** `GravitySystem` + `GravityPendingStore` (ADR §57). No falling_block actor. |
| 7 | **Playerdata reconnect** (`pd:`) | **Shipped:** pose + GameMode in world LevelDB (ADR §60). No `players/` volume. |
| 8 | **World beyond flat** | **Shipped (Zenith leaf):** §62–§67 provider/noise/biomes/caves/ore; §70 join terrain; §71 Dimension seam; §72 FastNoiseLite + anti-pillar height. **§61 Mojang seam = later** (not blocking H1 close). |

If two contributors pick work, prefer **different** [Yes-next](#yes-next-after-h1) rows — not both building command/plugin infrastructure.

**Platform health (not H1 product):** see [`robustness-dx-debt.md`](robustness-dx-debt.md) and ADR §54. **Delivery risks (hard):** dirty&gt;remote, SoftCap honesty, leaf CI vs missing Bedrock E2E.

**Foundation:** StackId + DigProfiles (ADR §55) — shipped. **Join wire fidelity** (ADR §59) — shipped in `v0.0.2-alpha`.

**Release tag `v0.0.2-alpha`:** closed H1 rows 1–5 + §55/§56/§59. Post-tag on `develop`: gravity (§57), playerdata (§60), world seams (§62–§72). Dual-storage **direction** is §61 (not shipped).

**Protocol smoke bot (ADR §58):** [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) — Bun + bedrock-protocol, **separate repo**. Human Gate A remains the product smoke authority for tags.

---

## Yes-next (after H1)

Pick **one** axis. Do **not** open plugins/JS, Spectator, custom biomes, FormSystem, hunger/damage completo, or Serenity traits/ECS without ADR + demonstrated need (see Horizon 2).

| Priority | Leaf | Notes |
|------:|------|--------|
| 0 | **Human smoke — fresh noise world (§72)** | Delete/`c:`-clear world; Gate A look for pillar columns. Process gate before more gen ADRs. |
| 1 | **Survival honesty (small)** | **Recommended next product code:** Q-throw and/or floor-drop despawn TTL, **or** death drops (inventory currently kept on void death). One ADR + one PR. Unlocks LAN “prova de fogo” without Mojang import. |
| 2 | **Smoke-bot `first10` green** | Regression proof (ADR §58). Reduces bus factor; not a gameplay feature. |
| 3 | **§61 Mojang spike** | Only when the goal is open/import PM/BDS worlds. Seam + offline converter — never replace ZLDB default. |
| 4 | **AuthInput × client FPS** | Measure-only ([`robustness-dx-debt.md`](robustness-dx-debt.md)); coalesce only after numbers. |

**Default stance:** priority **0 → 1** (smoke gen, then survival leaf). §61 is opt-in adoption work, not the default “next.”

---

## Horizon 2 — Later (explicitly not “next”)

Pull only with ADR + demonstrated need:

- Full creative catalogue / `block_state_b64`
- 3×3 crafting table / Mojang recipe dump
- Hunger / food / damage pipeline (beyond spawn attributes)
- Armor / ender chest / hoppers
- Stairs/beds/doors generic block-state facing stack
- Multi-world load / Mojang world format (**direction:** §61 seam + converter — not ZLDB replacement)
- Plugin API / DI / VisibilitySystem / EventBus product surface beyond login/quit (§21)
- `/` command **framework** + Bedrock autocomplete
- Automating protocol schema CI / full load harness
- Nether/End Dimensions; custom biome authoring; FormId product UI

---

## Where Zenith still lags (honest)

| Audience goal | Prefer instead | Zenith stance |
|---------------|----------------|---------------|
| Plugins tomorrow, ops mature | **PocketMine / Nukkit** | Intentional freeze until domains stable ([`comparison.md`](comparison.md)) |
| Vanilla fidelity, zero source | **BDS** | We own transport + tick; not Realms clone |
| “Works like PM for survival SMP” | Wait / other stack | Missing mobs, redstone, full damage/hunger, Mojang worlds, years of edge cases |
| Layered .NET platform, LAN spine, leaf libs | **Zenith** | Winning on structure/DX trajectory, not checklist length |

**Behind PM/Nukkit on:** extensibility economy, edge-case years, “just run a server,” gameplay completeness.  
**Ahead / deliberate on:** decide≠transmit≠serialize, owned Nbt/LevelDB leaves, documented freeze, overlay world + Dimension seam, sparse IO.

Do not measure success as “how many PM plugins we can host.” Measure success as **honest LAN multiplayer** → **honest extension points on Gameplay**.

---

## Contributor map

```text
Pick work          →  this file (Yes-next) + alpha-gate if pre-tag
Architecture       →  ARCHITECTURE.md + AGENTS.md (folders)
Why / Deferred     →  docs/decisions.md (ADR before new layer)
How to open PR     →  CONTRIBUTING.md + issue/PR templates
```

Anti-patterns for drive-by PRs: recreate `Network/`; add Scheduler/ECS/plugin bus “for cleanliness”; start `/` autocomplete or §61 before a Yes-next survival/smoke leaf is chosen.

---

## Maintenance

When tagging alpha or merging a Yes-next leaf: tick/adjust rows here and link the ADR. Prefer deleting stale “next” bullets over accumulating a second Deferred encyclopedia.

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
| 2 | **Death / Respawn** | **Shipped:** void → DeathInfo + Respawn handshake (§40); Survival death loot → floor (§73). Soft-rescue retired. No full damage pipeline. |
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

**Release tag `v0.0.2-alpha`:** closed H1 rows 1–5 + §55/§56/§59. Post-tag on `develop`: gravity (§57), playerdata (§60), world seams (§62–§72), survival/dig honesty (§73–§74), cave-gen perf (§75), packet wire codegen (§76). Dual-storage **direction** is §61 (not shipped).

**Protocol smoke bot (ADR §58):** [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) — Bun + bedrock-protocol, **separate repo**. Human Gate A remains the product smoke authority for tags.

---

## Shipped since H1 close (platform work, not new horizon rows)

Not gameplay leaves — recorded here so this file stays the accurate "what actually landed" list instead of drifting behind `git log`.

| ADR | Leaf | Notes |
|----:|------|-------|
| §73 | **Floor-drop honesty** | Q-throw / ISR Drop, Survival death loot to floor, `TryAddOrMerge` no silent clobber. No gravity/despawn TTL/XP orbs. |
| §74 | **Dig/chest honesty** | Wrong-tool no-drop, Creative chest content dump, death SoftCap keeps slot on refuse. No loot tables/durability. |
| §75 | **Cave-gen alloc cut** | `OverworldCaveContext` disposable + ThreadStatic scratch + `ArrayPool` — cut §69 cave-context GC pressure. No carve-semantics change. |
| §76 | **Packet wire codegen** | Roslyn incremental source generator (`[GamePacket]` + `[Wire*]` attributes) mechanizes `Encode`/`Decode` boilerplate. 31/64 packet types migrated so far (opt-in, Tier B packets stay hand-written by design). `tools/protocol-import` (Mojang + Endstone schema sources) scaffolds new migrations and diffs protocol drift. |

---

## Yes-next (after H1)

Pick **one** axis. Do **not** open plugins/JS, Spectator, custom biomes, FormSystem, hunger/damage completo, or Serenity traits/ECS without ADR + demonstrated need (see Horizon 2).

| Priority | Leaf | Notes |
|------:|------|--------|
| 0 | **Human smoke — fresh noise world (§72)** | Delete/`c:`-clear world; Gate A look for pillar columns. Still owed — code shipped and later gen work (§75) landed on top of it, but the human confirmation pass itself isn't recorded as done. Do this before opening another *terrain* ADR (perf/infra leaves like §75/§76 are not gated by this). |
| 1 | **Packet codegen migration, remaining Tier A** | §76 shipped the generator; 33/64 packets still hand-written and eligible (Tier B list in ADR §76 stays hand-written on purpose — don't force those). Plain migration PRs, no new ADR needed per packet. |
| 2 | **Smoke-bot `first10` green** | Regression proof (ADR §58). Reduces bus factor; not a gameplay feature. |
| 3 | **Floor-drop despawn TTL** | Optional remainder of §73 — drops never disappear today. |
| 4 | **§61 Mojang spike** | Only when the goal is open/import PM/BDS worlds. Seam + offline converter — never replace ZLDB default. |
| 5 | **AuthInput × client FPS** | Measure-only ([`robustness-dx-debt.md`](robustness-dx-debt.md)); coalesce only after numbers. |
| 6 | **EventBus second consumer (proposed, needs ADR)** | Not a plugin API. `EventBus` today only `Publish`es login/quit with zero domain consumers (ARCHITECTURE.md notes). Pick one existing Gameplay moment (e.g. death/respawn or chat) and wire a **second** internal `Subscribe<T>` consumer through it, to prove the seam holds under >1 listener before any plugin surface is promised. See "Competitive read" below for why this is next, not Horizon 2 plugin API itself. |

**Default stance:** priority **0 → 1 → 2** (confirm the terrain smoke gate, keep chipping at codegen migration, then smoke-bot). §61 is opt-in adoption work, not the default “next.” **6** is the proposed step *after* 0–2 close — flagged now so it doesn't get skipped straight to Horizon 2's full plugin API.

### Competitive read (Aug 2026)

Cross-checked against local clones of comparable/newer projects (`dx.md` § "Bedrock references"): **Basalt** (C#/.NET, comparable age to Zenith) already ships plugin support, enchantments, hoppers, and forms — ahead of Zenith on breadth precisely because it didn't gate features behind an architecture-first bet. Dragonfly and Endstone, the two projects whose extension models are most mature, only got there because their event/handler seam was proven with real internal consumers *before* any plugin promise. Priority 6 is that proof step for Zenith — small, ADR-gated, reversible — not a decision to open plugins.

---

## Horizon 2 — Later (explicitly not “next”)

Pull only with ADR + demonstrated need:

- Full creative catalogue / `block_state_b64`
- 3×3 crafting table / Mojang recipe dump
- Hunger / food / damage pipeline (beyond spawn attributes)
- Armor / ender chest / hoppers
- Stairs/beds/doors generic block-state facing stack
- Multi-world load / Mojang world format (**direction:** §61 seam + converter — not ZLDB replacement)
- Plugin API / DI / VisibilitySystem / EventBus product surface beyond login/quit (§21) — **stepping stone:** [Yes-next priority 6](#yes-next-after-h1) proves the `EventBus` seam with a second internal consumer first
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

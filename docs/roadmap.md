# Roadmap (lean)

**Purpose:** shared order of work for humans and agents. **Not** PocketMine feature parity and **not** a second copy of every Deferred line in [`decisions.md`](decisions.md).

Update this file when a horizon closes (e.g. alpha tagged) or an ADR changes “yes-next”. Day-to-day constraints stay in [`ARCHITECTURE.md`](../ARCHITECTURE.md).

---

## Horizon 0 — Closed: `v0.0.1-alpha`

Tagged **v0.0.1-alpha** (Jul 2026). `main` and `develop` both at the release tip. Notes: [`release-notes-template.md`](release-notes-template.md) / [GitHub Release](https://github.com/zenith-bedrock/zenith/releases/tag/v0.0.1-alpha). Gate: [`alpha-gate.md`](alpha-gate.md).

**Already shipped on that tag:** LAN spine (login → flat InMemory **or** LevelDB world → place/break → inventory/chests/craft → Creative/Survival → persist → dig crack + void rescue → graceful disconnect). Chest facing (§46), MOTD/session hygiene (§47), ordered fragment reassembly (raknet), folder layout (§48).

**Do not slide into post-tag polish as Horizon 0:** sand/gravel gravity, drop-entity wire, death/respawn, tools, double-chest, `/` commands, plugins, biomes — those are Horizon 1+ or explicit non-goals.

---

## Horizon 1 — After alpha: honesty gaps (suggested order)

Do these **as separate ADRs + PRs**. Earlier items unblock later ones.

| Order | Leaf | Notes |
|------:|------|--------|
| 1 | **Drop-entity wire** (minimal) | **Shipped** (Jul 2026): `AddItemActor` / `TakeItemActor`; FloorDropStore holds entity id (§26 adendo). No ECS/physics/despawn. |
| 2 | **Death / Respawn** | **Shipped** (Jul 2026): void → DeathInfo + Respawn handshake (§40 adendo); inventory kept. Soft-rescue retired. No damage pipeline / death drops. |
| 2b | **Block/item registry honesty** | **Shipped** (Jul 2026): palette reverse `runtimeId→name` + `Blocks.IsPlaceable` allowlist (§12 adendo). Before `/gamemode`. |
| 3 | **`/gamemode` minimal** | **Shipped** (Jul 2026): chat parse → intent → `GameModeSystem` + SetPlayerGameType/abilities/CreativeContent remint (§52). Not a command framework. |
| 4 | **Tool dig speed / efficiency** | **Shipped** (Jul 2026): DF `BreakDuration` + curated tools + 3602 on speed delta (§27 adendo). No enchants. |
| 5 | **Double-chest** (sneak-place + 54 UI) | **Shipped** in `v0.0.2-alpha` (ADR §56) — pair + 54 UI + 2×`ct:`. |
| 6 | **Block gravity** (sand/gravel) | **Shipped** (Jul 2026): `GravitySystem` + `GravityPendingStore`; discrete UpdateBlock falls (ADR §57). No falling_block actor. |
| 7 | **Playerdata reconnect** (`pd:`) | **Shipped** (Jul 2026): pose + GameMode in world LevelDB (ADR §60). No `players/` volume. |
| 8 | **World beyond flat** | Noise/biomes or import strategy — only when flat+overlay no longer answers LAN product questions. |

If two contributors pick from this list, prefer **different rows**, not both building command infrastructure.

**Platform health (not H1 product):** cross-thread dig/UI, Online-once, send/tick GC, handler split, DX cleanup — see [`robustness-dx-debt.md`](robustness-dx-debt.md) and ADR §54. Do not file those as Horizon‑1 rows. **Delivery risks (hard):** dirty&gt;remote, SoftCap honesty, leaf CI vs missing Bedrock E2E — recorded in that debt doc; do not soft-pedal.

**Foundation (not an H1 product row):** block/item **StackId + DigProfiles** (ADR §55) — **Shipped** (`3649391`, Jul 2026). Structural honesty so later leaves do not rewrite inventory identity.

**Join wire fidelity (ADR §59):** Session-owned `ClientProfile` + full join skin; `/gamemode` peer RefreshPeerView; server-authored LevelSound on place/break/hit — **shipped** in `v0.0.2-alpha`.

**Release tag `v0.0.2-alpha`:** closes H1 rows 1–5 + §55/§56/§59 on `main`/`develop`. Notes: [`release-notes-template.md`](release-notes-template.md). Gravity (§57) + playerdata (§60) shipped on develop after the tag. **Next leaf:** world beyond flat (H1#8) when product needs it.

**Protocol smoke bot (ADR §58):** [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) — Bun + bedrock-protocol, **separate repo** (no JS in the C# tree). Human Gate A remains the product smoke authority for tags.

---

## Horizon 2 — Later (explicitly not “next”)

Pull only with ADR + demonstrated need:

- Full creative catalogue / `block_state_b64`
- 3×3 crafting table / Mojang recipe dump
- Hunger / food / damage pipeline (beyond spawn attributes)
- Armor / ender chest / hoppers
- Stairs/beds/doors generic block-state facing stack
- Multi-world load / Mojang world format
- Plugin API / DI / VisibilitySystem / EventBus product surface beyond login/quit (§21)
- `/` command **framework** + Bedrock autocomplete
- Automating protocol schema CI / full load harness

---

## Where Zenith still lags (honest)

| Audience goal | Prefer instead | Zenith stance |
|---------------|----------------|---------------|
| Plugins tomorrow, ops mature | **PocketMine / Nukkit** | Intentional freeze until domains stable ([`comparison.md`](comparison.md)) |
| Vanilla fidelity, zero source | **BDS** | We own transport + tick; not Realms clone |
| “Works like PM for survival SMP” | Wait / other stack | Missing entities, death, tools, gen, redstone, … by years |
| Layered .NET platform, LAN spine, leaf libs | **Zenith** | Winning on structure/DX trajectory, not checklist length |

**Behind PM/Nukkit on:** extensibility economy, edge-case years, “just run a server,” gameplay completeness.  
**Ahead / deliberate on:** decide≠transmit≠serialize, owned Nbt/LevelDB leaves, documented freeze, overlay world model.

Do not measure success as “how many PM plugins we can host.” Measure success as **honest LAN multiplayer** → **honest extension points on Gameplay**.

---

## Contributor map

```text
Pick work          →  this file (horizon) + alpha-gate if pre-tag
Architecture       →  ARCHITECTURE.md + AGENTS.md (folders)
Why / Deferred     →  docs/decisions.md (ADR before new layer)
How to open PR     →  CONTRIBUTING.md + issue/PR templates
```

Anti-patterns for drive-by PRs: recreate `Network/`; add Scheduler/ECS/plugin bus “for cleanliness”; start `/` autocomplete before drop-entity/death story exists.

---

## Maintenance

When tagging alpha or merging a Horizon‑1 leaf: tick/adjust rows here and link the ADR. Prefer deleting stale “next” bullets over accumulating a second Deferred encyclopedia.

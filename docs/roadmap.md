# Roadmap (lean)

**Purpose:** shared order of work for humans and agents. **Not** PocketMine feature parity and **not** a second copy of every Deferred line in [`decisions.md`](decisions.md).

Update this file when a horizon closes (e.g. alpha tagged) or an ADR changes “yes-next”. Day-to-day constraints stay in [`ARCHITECTURE.md`](../ARCHITECTURE.md).

---

## Horizon 0 — Now: close `v0.0.1-alpha`

**Single source of gate checks:** [`alpha-gate.md`](alpha-gate.md).

Focus: prove LAN multiplayer spine (login → flat world → inventory/chests/craft → persist → crack/void smokes) and ship the tag. Do **not** open plugins, `/` frameworks, biomes, or vanilla worlds in this horizon.

Useful parallel leaves that already belong near the spine (keep PRs small):

| Leaf | Why it fits now |
|------|-----------------|
| Deploy / smoke follow-ups | Fragment reassembly, MOTD/session hygiene, chest facing — validate on real clients |
| Protocol mismatch UX / logging hygiene | Ops clarity without new gameplay domains |
| Docs hygiene | CONTRIBUTING, AGENTS, this roadmap |

---

## Horizon 1 — After alpha: honesty gaps (suggested order)

Do these **as separate ADRs + PRs**. Earlier items unblock later ones.

| Order | Leaf | Notes |
|------:|------|--------|
| 1 | **Drop-entity wire** (minimal) | Floor drops already exist as cells (§26); peers need to *see* items. Minimal dropped-item entity only — not ECS/mobs (§32). |
| 2 | **Death / Respawn** | Soft void rescue is not death. Needs packets + clear softlock rules (§40 Deferred). |
| 3 | **`/gamemode` minimal** | One command path, config-backed modes you already have — **not** a command framework or autocomplete stack ([`dx.md`](dx.md) freeze). ADR first. |
| 4 | **Tool dig speed / efficiency** | Extends §27; still no enchants catalogue dump. |
| 5 | **Double-chest** (sneak-place + 54 UI) | Facing-only (§46) does not unlock this; pair model + store (§28/§39). |
| 6 | **`players/` or position persist** | After identity/reconnect story is clear; no silent path reinterpret (§20). |
| 7 | **World beyond flat** | Noise/biomes or import strategy — only when flat+overlay no longer answers LAN product questions. |

If two contributors pick from this list, prefer **different rows**, not both building command infrastructure.

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

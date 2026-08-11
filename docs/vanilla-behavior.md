# Vanilla behavior guide (tester-facing)

**Purpose:** a quick reference for anyone joining Zenith to test — what's implemented and *should* match vanilla Bedrock, what's known to differ or is missing outright, and where to report anomalies. This is **not** a new spec: it's a reading of [`roadmap.md`](roadmap.md), [`alpha-gate.md`](alpha-gate.md), and [`decisions.md`](decisions.md) aimed at people who don't want to dig through ADRs first.

If you hit something that isn't listed as a known gap below, it's likely a real bug — please open an issue (see [`CONTRIBUTING.md`](../CONTRIBUTING.md)) with steps to reproduce, protocol version, and client version.

---

## How to read this doc

- ✅ **Should match vanilla** — implemented, expected to behave like Bedrock. Deviations here are bugs.
- ⚠️ **Partial / simplified** — implemented but intentionally narrower than vanilla (curated lists, no edge cases). Not a bug unless it breaks the documented scope.
- ❌ **Not implemented** — vanilla behavior does not exist yet. Don't file "missing feature" issues for these; check [`roadmap.md`](roadmap.md) first.

---

## ✅ Should match vanilla — good bug-hunting targets

| Area | Notes |
|------|-------|
| Login / join handshake | RakNet + login → spawn in flat or LevelDB world. Chat and player visibility to nearby peers. |
| Block place / break | Overlay writes (`ov:`) on top of base flat/generated terrain; peers see updates live. |
| Survival dig timing | `BreakDuration` math (Dragonfly-derived) + curated tool speed table; crack progress visible to peers (ADR §27). |
| Inventory (36 slots) | Rearrange (ISR), held-item sync to peers, Creative palette → cursor / SHIFT → bag. |
| Chests | Single + double chest (sneak-place to pair), 54-slot UI, persist to `ct:`. |
| 2×2 crafting | Survival craft chain (e.g. planks → chest). |
| Creative / Survival gamemode | `/gamemode` minimal — fly in Creative, no fly in Survival. |
| Void death / respawn | Fall below world → DeathInfo + Respawn handshake; Survival death drops loot to floor. |
| Block gravity | Sand/gravel fall when unsupported (`GravitySystem`). |
| World persistence | LevelDB overlay + inventory/chest/playerdata persist across restart; graceful shutdown flushes (Ctrl+C / SIGTERM). |
| Terrain beyond flat | Noise/biome/cave/ore generation (§62–§67, §72) when configured — watch for pillar/column artifacts. |

If any of these misbehave — desync, wrong slot contents, block not appearing for a peer, crash on a normal action — that's exactly the kind of report that's useful right now.

---

## ⚠️ Partial / simplified — expected narrower than vanilla

| Area | What's simplified |
|------|--------------------|
| Block/item registry | Palette-driven `runtimeId → name`, but only a curated allowlist is placeable/diggable. Not every vanilla block works yet. |
| Creative catalogue | Short curated list, not the full Mojang `creative_items.json`. |
| Tool speed | Curated tool table + speed-delta packet; **no enchantments**. |
| Drop entities | Minimal `AddItemActor`/`TakeItemActor` wire — no physics, no despawn timer yet. |
| World gen | Zenith-native noise/biome/cave/ore — not Mojang's noise tables. Visuals will differ from real Bedrock terrain. |

Anomalies here should only be reported if they break the *documented* scope (e.g. a curated tool doesn't apply its speed bonus at all) — not "X isn't in the curated list yet."

---

## ❌ Not implemented — don't file these as bugs

Full list of non-goals: [`decisions.md`](decisions.md) § "Explicit non-goals" and [`alpha-gate.md`](alpha-gate.md) § "Explicit non-goals on the tag". Highlights:

- Mobs / entity AI (planned for later — see project notes below)
- Hunger / food / full damage pipeline
- Redstone
- Armor, ender chest, hoppers
- Full vanilla command catalogue / plugin-facing command framework (Zenith currently has a small protocol-independent command core with Bedrock metadata for representative commands)
- Plugin API
- Nether / End dimensions, custom biome authoring
- Mojang world import (BDS/PM world format)
- Enchantments

---

## Where to look before reporting

1. [`roadmap.md`](roadmap.md) — is this feature shipped, "yes-next," or Horizon 2 (later)?
2. [`alpha-gate.md`](alpha-gate.md) — manual smoke checklist and known ops traps (e.g. empty `world.path` = InMemory, no warning).
3. [`decisions.md`](decisions.md) — search `ADR §N` referenced in code comments for the *why* behind a behavior.

If it's in the ✅ table and still broken, that's a real bug report. If it's ⚠️ or ❌, check the linked docs first — it may be intentional scope, not a defect.

---

**Maintenance:** update this doc's tables whenever a `roadmap.md` row moves from "yes-next" to shipped, or a Horizon 2 item gets pulled in with an ADR. Keep it short — this is a map to the other docs, not a duplicate of them.

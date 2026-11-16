# Vanilla behavior guide (tester-facing)

**Purpose:** a quick reference for anyone joining Zenith to test — what's implemented and *should* match vanilla Bedrock, what's known to differ or is missing outright, and where to report anomalies. This is **not** a new spec: it's a reading of [`roadmap.md`](roadmap.md), [`alpha-gate.md`](alpha-gate.md), and the [decision records](adr/README.md) aimed at people who don't want to dig through ADRs first.

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
| Crafting (2×2 personal + 3×3 table) | Survival craft chain (log → planks → stick/tools/chest/fence/…). Recipes whose real vanilla shape needs more than 2×2 (chest, every tool, fence, wall) require an actual crafting table, matching vanilla — placing/interacting with a `crafting_table` block opens a real 9-slot grid (ADR §139). |
| Creative / Survival gamemode | `/gamemode` minimal — fly in Creative, no fly in Survival; no Adventure/Spectator semantics. |
| Void death / respawn | Fall below world → DeathInfo + Respawn handshake; Survival death drops loot to floor. |
| Block gravity | Sand/gravel fall when unsupported (`GravitySystem`): an unsupported block hangs for 2 ticks (the vanilla scheduled-tick delay, to verify on BDS) and then becomes a falling entity; a stack peels one block per delay. |
| Experience orbs | A player-caused kill drops `xp_orb` entities in the vanilla sizes; they home in on the nearest player within 8 blocks, are collected on touch (points credited once, pickup sound), and expire after 5 minutes. RAM-only. No merging, and mobs are the only source of experience (`ExperienceOrbSystem`). |
| World persistence | LevelDB overlay + inventory/chest/playerdata persist across restart; graceful shutdown flushes (Ctrl+C / SIGTERM). |
| Terrain beyond flat | Noise/biome/cave/ore generation (§62–§67, §72) when configured — watch for pillar/column artifacts. |

If any of these misbehave — desync, wrong slot contents, block not appearing for a peer, crash on a normal action — that's exactly the kind of report that's useful right now.

---

## ⚠️ Partial / simplified — expected narrower than vanilla

| Area | What's simplified |
|------|--------------------|
| Mobs | Ten species (zombie, skeleton, spider, creeper, enderman, bat, villager, golem, fish, cow) plus minecart and projectile run on the ECS with melee, knockback, hurt/death feedback and data-driven loot tables (`data/loot_tables.json`). Spawning, breeding and per-species special behaviour are narrower than vanilla — see [`entity-fidelity.md`](entity-fidelity.md) and [`entity-behavior-matrix.md`](entity-behavior-matrix.md). |
| Hunger / armor | Hunger and armor reduction exist; armor durability wear, effects and saturation edge cases are incomplete. |
| Blocks | Cubes, glass/panes, logs/pillars, stairs, slabs, torches and ground plants are placeable/breakable with derived tools, hardness and drops — see [`blocks.md`](blocks.md) for what is and is not covered (doors, containers, liquids, walls, redstone are not). Gravity covers sand/gravel only. |
| Creative catalogue | The full vanilla `creative_items.json` is loaded and sent; what a catalogue entry *does* once placed is limited to the supported block kinds (see [`blocks.md`](blocks.md)). |
| Tool speed | Curated tool table + speed-delta packet; **no enchantments**. |
| Drop entities | Floor items use `AddItemActor`/`TakeItemActor` with a fan-out store; physics is minimal. |
| World gen | Zenith-native noise/biome/cave/ore — not Mojang's noise tables. Visuals will differ from real Bedrock terrain. |

Anomalies here should only be reported if they break the *documented* scope — not "X isn't in the curated list yet."

---

## ❌ Not implemented — don't file these as bugs

The prioritized gap list lives in [`../plans/dragonfly-gap-analysis-2026-11-15.md`](../plans/dragonfly-gap-analysis-2026-11-15.md). Highlights:

- Dynamic world: random/scheduled ticks, liquids, fire, crop/tree growth, weather
- Nether / End dimensions (dimension ids exist only as protocol constants)
- Redstone, furnaces, hoppers, ender chest, beds, block entities beyond chests
- Enchantments, potion effects, bows/ranged player weapons, XP from sources other than mob kills
- Permissions, admin commands, plugin API, full command catalogue
- Forms, title/bossbar/scoreboard
- Mojang world import (BDS/PM world format)

---

## Where to look before reporting

1. [`roadmap.md`](roadmap.md) — is this feature shipped, "yes-next," or Horizon 2 (later)?
2. [`alpha-gate.md`](alpha-gate.md) — manual smoke checklist and known ops traps (e.g. empty `world.path` = InMemory, no warning).
3. [`adr/`](adr/README.md) — open the `ADR §N` referenced in code comments for the *why* behind a behavior.

If it's in the ✅ table and still broken, that's a real bug report. If it's ⚠️ or ❌, check the linked docs first — it may be intentional scope, not a defect.

---

**Maintenance:** update this doc's tables whenever a `roadmap.md` row moves from "yes-next" to shipped, or a Horizon 2 item gets pulled in with an ADR. Keep it short — this is a map to the other docs, not a duplicate of them.

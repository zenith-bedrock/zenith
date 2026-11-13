# ADR §31 — Creative mode v1 (config → join)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §31`.

**Choice:**

1. `ServerConfig.Validate()` accepts only `"Survival"` / `"Creative"` (loud fail; no silent fallback).
2. Every joiner gets that single mode from config: `Player.GameMode` plus both `StartGamePacket.GameMode` (PlayerGameMode) and `GameType` (WorldGameMode) from the same config value. Runtime Survival↔Creative is §52 (`/gamemode`); Adventure/Spectator still out.
3. Creative join: empty hotbar (client Creative UI supplies items). Survival: existing starter seed.
4. `CreativeContentPacket` after `ItemRegistry`, before CraftingData / BiomeDefinitionList — short list from `CreativeCatalog`. Do **not** parse `creative_items.json` (`block_state_b64`). Sent on **every** join regardless of Survival/Creative (catalog seed; UI gated by gamemode — same as PM/DF/Serenity).
5. `BlockSystem.ApplyEdit` branches: Creative place skips `TryConsumeOne`; Creative break skips timing + **block** inventory/floor loot (still clears chest store). **Chest contents → floor even in Creative (§74)** — never silent void of `ct:`. Survival path unchanged until §74 loot gate.

**Why:** LAN feedback needs Creative UI + infinite place/break without inventing entity wire or admin commands. Uniform config mode is the only source of truth while `/` stays frozen.

**Deferred:** Adventure/Spectator; pick-block; rich creative tabs; full creative list / `block_state_b64`; craft ISR quirks unique to Creative.

**Adendo (jul 2026):** Runtime `/gamemode` → §52 (overrides “no runtime switch” above for Survival↔Creative only).

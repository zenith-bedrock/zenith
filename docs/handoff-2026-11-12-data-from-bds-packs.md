# Handoff — data from BDS packs, crafting, inventory audit (2026-11-12)

Read first: `AGENTS.md`, `docs/inventory-audit.md`, ADR §146 in `docs/decisions.md`, `tools/bedrock-data/README.md`.
User writes Portuguese; reply in Portuguese. Use they/them for unknown pronouns.

## Standing rules from the user
- **Zenith is not assumed correct.** Containers/inventory/etc. have known bugs; a passing test or smoke is not proof. Always say what was verified vs not.
- Priorities: separation of responsibilities, decoupling, DX; deep refactors are welcome. **No legacy/gambiarra**: data must be coherent, derived from the real source, no silently dropped or inert entries.
- Work directly on `develop` (zenith) / `main` (smoke bot). Commit trailers: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` + the session's `Claude-Session:` line. **Push only with explicit approval.**
- e2e is set aside for now; focus on Zenith itself. Smoke bot is a real-ish client reference, not an oracle for Zenith correctness.
- Workspace `G:\zenith-workspace`: `zenith` (C#), `zenith-smoke-bot` (Bun; user has uncommitted WIP in `src/zombie.ts`, do not commit it; `floorPickup.ts` is line-ending noise), `essential-reference` (PHP, untrusted, never run), `cinnabar-reference` (PolyForm Noncommercial: behaviour reference only, never copy code/data), `mojang-protocol-docs`, `bds-reference`.
- Tooling quirks: no `pkill` (use PowerShell `Get-Process zenith|bedrock_server | Stop-Process -Force`); building while `zenith.exe` runs fails; Bash heredocs with quotes/C# are fragile, prefer Write/Edit; Python on Windows resolves `/tmp` to `C:\tmp`; `python -I` hides the script dir (use `PYTHONPATH=.`).

## Git state
- `origin/develop` = `2e1cb70`. **Local, unpushed, awaiting approval:**
  - `3cc3b07` real vanilla recipes matched by shape/name/tag; CraftingData sends shaped + tags
  - `464e6c3` CraftingData shield output carries `blocking_tick`
  - `a33990a` `bds_pack.py` + coverage report (the report script was removed in the next commit)
  - `5ed0025` recipes built from BDS packs + generators, oracle-verified
  - plus this handoff file (uncommitted unless committed afterwards)
- Smoke bot origin/main `7e1d7ca`.

## What was learned about BDS (all observed on BDS 1.26.63.1 at `bds-reference/bds`)
- BDS layers `behavior_packs/vanilla` + `vanilla_<version>` (57 packs) in version order; same file name in a later pack replaces the earlier. Files ship as `.brarchive` (magic `7d2725b1a0527026`, u32 count, u32 version, 256-byte records: u8 len, name, u32 offset, u32 size; data after the table). Reader: `tools/bedrock-data/bds_pack.py`.
- Recipes, items, item_catalog (creative groups), biomes, entities, loot, trading, spawn rules, structures and the 98 newest data-driven blocks are plain pack JSON. Classic blocks and the legacy `(name, data)` flattening table live in the exe (not read yet).
- BDS also registers recipes in code (colour dyeing, slabs, buttons...). The wire `CraftingData` has junk `name:aux` descriptors (e.g. `minecraft:red_wool:0`) — never copy those.
- Wire facts: shield item extra carries a trailing `blocking_tick` (i64) (fixed for CraftingData via `NetworkItemStack.IsShield`; `ItemV4` writers still send an empty extra blob, would mis-parse for a shield stack).
- Captures (decoded packets from a stock BDS via the bot) are in `bds-reference/out/` (`crafting_data.json`, `item_registry.json`, `creative_content.json`, `biome_definition_list.json`, `available_commands.json`); capture script `bds-reference/capture.ts` (copy into the bot dir as `_capture.ts`, run with `bun run`, env `OUT/V/P`). BDS config: port 19140/19141, offline, `transport=raknet`. BDS and Zenith both use 19132 for LAN discovery: stop one before running the other.

## What is done (verified)
- `tools/bedrock-data/extract_recipes.py <bds root> <item_registry.json> <out.gz> --verify <capture dir>` builds `src/zenith/data/recipes.json.gz`: 1101 shaped + 2353 shapeless, keyed by item name, no ids baked. `--verify` (capture as oracle only): 3672 aux-free wire grid recipes, **0 missing**; 458 wire recipes need an item aux model and are not emitted.
- `RecipeRegistry` resolves names against Zenith's palettes (`Blocks.TryGetDefaultRuntimeId`, item palette); unknown name = `InvalidDataException`. No `StripAux`, no silent skips.
- `zenith.Tests`: 1197/1197. Live: the bot captures the full `crafting_data` from Zenith with the new set. Not validated with a retail client.
- Earlier-this-session pushed work: protocol 2225 wire deltas, leaf-lib extractions, smoke bot speaks 2225 natively.

## Open work, suggested order
1. **Item aux/damage model** (`StackId` has no aux): unlocks the 458 recipes (beds, banners, firework stars, tipped arrows, suspicious stew, maps), the shield in inventory writers, durability (`MineBlock` currently echoes without mutating). Needs ADR first.
2. **Move the other catalogs to the pack source** the same way: item palette/components (`items`, `item_catalog` = creative groups), biomes, loot, trading. Today embedded block palette / item registry / creative / biome / jigsaw are BDS 1.26.52 (protocol 2193); update `ServerIdentity.EmbeddedDataProtocolVersion` together with any refresh. Tools: `tools/bedrock-data` (`import_data.py`, endstone plugin).
3. **Blocks:** classic block states are only in the exe. Options: keep the palette dump, or reverse-engineer the exe (py + capstone; user allowed it). Not decided.
4. `docs/inventory-audit.md` known gaps, each to become a failing test first: `CraftRecipeAuto` always rejected (recipe book can't craft; needs ingredient allocation over grid + inventory), `Consume`/`CraftResultsDeprecated` are no-ops, no offhand container (34), only chest/personal/crafting-table windows, `count==0` means whole stack. Then item-conservation property tests across all stores, and capture real ISR traffic with Cinnabar (needs the user's MS login/EULA).
5. Minor: `ServerConfig.cs` still uses NetherNet's `IceServer.Parse` for validation; NetherNetTransport sent stats are 0; retail-client validation of NetherNet and 2225 not done.
6. Ask the user to approve pushing the local commits (the repo's rule: close audit gaps and push the same day).

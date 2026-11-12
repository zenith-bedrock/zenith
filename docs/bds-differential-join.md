# Zenith vs stock BDS: what each sends to the same client at join

**How this was made (2026-11-12).** The same offline bot (`bedrock-protocol`, version 1.26.60 schema, protocol 2225) joined
Zenith (flat world, fresh `ZENITH_DATA`) and the stock BDS 1.26.63.1 (`bds-reference/bds`, default world), stayed 8 s after
spawn and recorded every parsed packet. 628 packets from Zenith, 984 from BDS, 0 decode errors on either side. World-dependent
traffic (chunks, entities, movement) was not compared. **This is a list of observed differences, not a verdict**: BDS is the
reference for what a client expects, but some differences are configuration (difficulty, view distance) and some may be
harmless. Nothing here was checked with a retail client. The capture script was a throwaway (it lived in the smoke bot's
`capture/` and was deleted); the method is easy to redo from `bds-reference/capture-damage.ts`.

## Packets BDS sends during join that Zenith never sends

| Packet | What it carries | Why it may matter |
|---|---|---|
| `unlocked_recipes` | the recipe book contents (`initially_unlocked`, thousands of ids) | With an empty book the client has no recipe to click, so the recipe-book path (`CraftRecipeAuto`, ADR 146/§auto-craft) that Zenith implements may never be exercised by a real client. |
| `available_entity_identifiers` | NBT list of every entity identifier | Client-side entity definitions; absent here. |
| `game_rules_changed` + `start_game.gamerules` | 39 gamerules (`keepInventory`, `fallDamage`, `showCoordinates`, `doDayLightCycle`, ...) | Zenith's `start_game.gamerules` is **empty**, so the client believes every rule is at its client default and there is no server rule model behind it at all. |
| `sync_world_clocks` (x2) | the 1.26 world clock sync | Zenith uses repeated `set_time` instead (every ~1 s). Whether the client still honours `set_time` for the day cycle was not checked. |
| `sync_entity_property` (x13) | synced entity properties | Needed for entities that define properties; absent. |
| `player_list` (x2) | the joining player's own record | Zenith sends player records only for peers. |
| `voxel_shapes` | collision shapes | Client prediction. |
| `player_hotbar` | selected slot | Zenith never tells the client its selected slot. |
| `set_difficulty`, `set_commands_enabled` | | Duplicates of `start_game` fields in BDS, absent in Zenith. |
| `trim_data`, `camera_presets`, `camera_aim_assist_presets`, `camera_spline`, `player_fog`, `current_structure_feature`, `level_event_generic`, `set_health` | | Smithing trims, camera system, fog, structure feature, health. |
| `server_to_client_handshake` | encryption handshake | BDS encrypts even with `online-mode=false`; Zenith does not (a design choice, not checked). |

## Status after the fixes (2026-11-12)

Each of these now matches the BDS capture, and every one has a golden test against the captured bytes; none was checked with a
retail client, and the re-capture below shows what the bot sees, not what the game renders.

| BDS packet | Zenith now | Source of the content |
|---|---|---|
| `voxel_shapes`, `sync_entity_property` x13, `available_entity_identifiers`, `player_fog`, `camera_presets`, `camera_aim_assist_presets`, `camera_spline`, `trim_data` | replayed verbatim at BDS's positions | `bedrock-data join-static` -> `data/join_static/` (manifest records BDS build, protocol, sha256) |
| `game_rules_changed` + `start_game.gamerules` | the 39 rules, modelled (`Rulebook/GameRules`), replicated on every change, persisted in the world store | defaults, order and types read from the capture, not typed in |
| `sync_world_clocks` (state, then registry) | sent at the BDS positions with our world time; the six time markers and ids are BDS's | capture, decoded and re-encoded byte for byte |
| `unlocked_recipes` | BDS's initially-unlocked list kept to recipes this server has (2 of 20: the other 18 are furnace recipes Zenith lacks) | capture |
| `set_difficulty`, `set_commands_enabled`, `player_hotbar`, `set_health` | sent | bodies equal the captured ones |

### What the game rules do (honestly)

All 39 rules are replicated, so the client sees BDS's defaults and reads the ones it handles itself (`showCoordinates`, ...).
Server side, **only four are enforced**: `keepInventory` (death keeps the items), `fallDamage`, `naturalRegeneration` and
`doTileDrops` (a broken block gives no item; a broken chest still drops its contents). `/gamerule <rule> [value]` shows or sets any
of the 39; the other 35 change the replicated value and nothing else: `doDayLightCycle` (world time is `tick % 24000` with no
pause), `doMobSpawning`, `mobGriefing`, `pvp`, `drowningDamage`, `fireDamage`, `doImmediateRespawn`, ... have no server behaviour
behind them. Rules persist across restarts (stored in the wire encoding under key `gr:`); a stored rule this build does not know,
or whose type changed, is dropped with a warning that names it.

### Left as they were, on purpose

- `player_list` for the player themself: omitted deliberately (`PlayerVisibility.AnnounceJoin`: the joining client builds its local
  actor without it, and it would replay the skin to its owner).
- `level_event_generic` (sleep state: `ableToSleep`, `overworldPlayerCount`, `sleepingPlayerCount`) and `current_structure_feature`:
  world-dependent, and Zenith has no sleeping or structure detection to report.
- `server_to_client_handshake`: Zenith does not encrypt.
- `enable_commands` / operator for everyone: a development default; the new `/gamerule` command relies on it (commands are parsed
  as operator because there is no per-player permission yet).
- The extra `inventory_content` sends (8 vs 4): sent on spawn release and again when the client reports it is initialised; not
  removed because nothing here can show whether a real client needs the second one.
- `respawn` (1 vs 3) and `update_abilities` (1 vs 2): sequencing, unchanged.
- `start_game` scalar diffs below: unchanged.

## `start_game` fields that differ

| Field | Zenith | BDS | Note |
|---|---|---|---|
| `gamerules` | `[]` | 39 entries | see above |
| `enable_commands` / `permission_level` | `true` / `operator` | `false` / `member` (cheats off) | Every Zenith player is an operator. |
| `block_pallette_checksum` | `0, 0` | real checksum | Zenith does not compute it. |
| `game_version` / `engine` | `1.26.62` | `*` / `1.26.63` | Zenith advertises .62 while the protocol is 2225. |
| `difficulty` | 0 (peaceful) | 1 (easy) | config |
| `achievements_disabled` | `false` | `true` | |
| `has_server_join_info`, `server_controlled_sound`, `rewind_history_size`, `limited_world_*`, `platform_broadcast_mode`, `xbox_live_broadcast_mode`, `enchantment_seed`, `seed`, `biome_name` | defaults/zero | filled | cosmetic or client-feature toggles; not individually checked |
| `entity_id` (unique id) | equals the runtime id (`2`) | distinct (`-42949672959`) | Zenith's unique and runtime ids are the same number. |
| `set_spawn_position` | `spawn_type: world`, dimension 0 | `spawn_type: player`, dimension 3, sentinel positions | BDS-specific encoding of "no explicit spawn"; Zenith sends a real position. |
| `network_settings.compression_threshold` | 256 | 1 | config |
| `chunk_radius_update` | 4 | 10 | config |

## Sequence differences

- Zenith sends `inventory_content` **8** times (BDS 4), `update_abilities` once (BDS twice) and `respawn` once (BDS three times:
  searching, searching, ready). The extra inventory contents are probably the two sends added for spawn and for the first
  tick; harmless but unexplained.
- BDS interleaves `player_list`, `sync_world_clocks`, `voxel_shapes` before `start_game`; Zenith sends `jigsaw_structure_data`
  then `start_game` directly.

## What Zenith does at join that BDS does not

- It spawns a **showcase of mobs around the origin at join** (zombie, minecart, skeleton, snowball, cow, creeper, enderman, bat,
  spider, villager, iron golem). In this capture the idle bot was killed by the zombie 3 s after spawn (`health` 20 -> 0,
  `death_info` + `respawn` at +5.3 s). That is a development scaffold, not a protocol difference, but a vanilla server does not do it.
- It drops floor items (`add_item_entity`, stone and dirt stacks of 64) at the spawn point.

## Not covered

Chunk contents and terrain, entity metadata, movement, every packet after the first 8 s, and anything a real client does with these
packets. The differences above say what Zenith leaves out; they do not say which omissions a client visibly tolerates.

# Developer experience (DX)

What it feels like to work in Zenith — today and as the tree matures.

## What you get today

### 1. A readable mental model

One sentence: decide → transmit → serialize → send.

That maps to folders and types. New contributors can ask “does this class **decide**?” and get an unambiguous answer. If it decides **and** serializes, the PR is wrong — not a style preference, an architectural rule.

### 2. Leaf libraries you can open in isolation

| Project | Use without the full server |
|---------|------------------------------|
| `src/raknet` | Reliable UDP + `BinaryStream` |
| `libs/nbt` (`Zenith.Nbt`) | LE / Network / BigEndian NBT round-trips |
| `libs/leveldb` (`Zenith.LevelDB`) | Put/Get/Delete/WriteBatch/Iterator for Zenith keys |

`dotnet test` on `nbt.Tests` / `leveldb.Tests` / `raknet.Tests` / `zenith.Tests` keeps format and transport bugs out of “boot the Bedrock client” loops.

### 3. Intent-first handlers

Inbound code validates and **queues**; systems apply on tick. That means:

- Reproducing “player placed block” does not require guessing thread races with RakNet
- Rate limits and bounds checks live next to decode, not deep in world mutation

### 4. Config that matches how you run binaries

`zenith.yml` next to the executable — same story in Visual Studio, `dotnet run`, or a Windows service. **Containers:** one product image ([`deploy/image/`](../deploy/image/)), env `ZENITH_DATA` (Compose/Dokploy `/data`, Wings `/home/container`), config at `$ZENITH_DATA/zenith.yml`. Empty `world.path` + `ZENITH_DATA` → LevelDB under that root (ADR §20). Platform adapters: [`deploy/compose/`](../deploy/compose/), [`deploy/pterodactyl/`](../deploy/pterodactyl/) (ADR §68). Wings may set **`SERVER_PORT`**. No other `ZENITH_*` env matrix. Log levels are split: `log.server` (default `info`) vs `log.raknet` (default `warn`) so enabling Bedrock Debug does not flood ACK/`Connected PID`.

IDE autocomplete/validation (optional): [`schemas/zenith.schema.json`](../schemas/zenith.schema.json) + workspace [`.vscode/settings.json`](../.vscode/settings.json) (Red Hat YAML / Cursor). **Not** used at boot — `ServerConfig.Validate()` remains the runtime SSOT.

### 5. License aimed at builders

LGPL-3.0: share improvements to the library; build applications on top with a clear story. See [`LICENSE`](../LICENSE).

### 6. Docs that admit trade-offs

[`ARCHITECTURE.md`](../ARCHITECTURE.md), this `docs/` tree, and [`libs/leveldb/README.md`](../libs/leveldb/README.md) document **non-goals** (unbounded overlays, single-table flush rewrite, no plugins yet). Surprises are worse DX than incomplete features.

Method naming (verb prefixes, no `Maybe*`): [`docs/naming.md`](naming.md).

## What we deliberately do **not** ship as DX yet

| Temptation | Why wait |
|------------|----------|
| Plugin API | Wrong extension surface early becomes forever |
| DI container | Hides dependency direction; architecture relies on it being visible |
| Command framework | Frozen; single `/gamemode` path only (§52) — no registry/autocomplete |
| “God” event framework | EventBus exists for login/quit; domain consumers wait for need |

Good DX is saying **no** until the yes is cheap to maintain.

Platform-health debt (Online-once, dig/UI on tick, send GC, …) lives in [`robustness-dx-debt.md`](robustness-dx-debt.md) — not Horizon‑1 product.

## Workflow suggestions

```text
1. Read docs/architecture.md + ARCHITECTURE.md (+ CONTRIBUTING.md / AGENTS.md for PRs and folders)
2. Touch the smallest leaf (`libs/nbt`, `libs/leveldb`, `src/raknet`) when possible
3. Add a unit test before a Bedrock client smoke when the change is format/protocol shape
4. Keep gameplay free of DataPacket / BinaryStream
5. If you need a new abstraction (Factory, ECS, Scheduler): justify against freeze list
6. Item wire: `NetworkItemStack` has three writers (`WriteNetworkItemStackDescriptor`, `WriteItemStackWrapper`, `WriteItemStack`). **Packet Encode picks** — never call a “default Write”. AddPlayer/AddItemActor → Wrapper; InventoryContent/MobEquipment → Descriptor; Creative/CraftingData → ItemStack. Inventory content / ISR OK: **`InventoryProtocol.DescribeForWire` only** (not Refresh+Get). Soft ISR match via `MatchesAdvertisedStackNetId` (§54).
7. Folders = roles under `src/zenith/` — look in `Packets/` / `Protocol/` / `Session/`, not a revived `Network/` junk drawer
8. Block→item bridge: Protocol maps via `Blocks.TryGetName` + `ItemPalette` only — reverse lookup must cover the dump (`BlockPalette.TryGetName`), not only curated `Blocks.*` consts. Placeables are an explicit allowlist (`IsPlaceable`), not “any palette rid”
9. Protocol packet shapes: see “Bedrock protocol docs” below before inventing field order
10. GameLoop fills Online once per tick (`FillOnline`); systems take `IReadOnlyList<Player> online` — do not call `PlayerManager.Online` inside nested peer loops
11. Block/item foundation (ADR §55): inventário usa `StackId` — nunca `int runtimeId` ambíguo. Overlay = só `BlockRuntimeId`. Três ids distintos: `BlockRuntimeId`, `ItemNetworkId`, `StackNetworkId` (ISR). Ver “Adding block/item capabilities” abaixo.
12. Double-chest (ADR §56): pairing is World adjacency+facing (`ChestPairing`); open UI is `OpenChestView` 27|54; persist stays two `ct:` blobs of 27 — no BlockActor.
13. Block gravity (ADR §57): sand/gravel only; `GravitySystem` after `BlockSystem`; sparse pending cells + UpdateBlock cascade — no Tile, no `AddActor` falling_block in MVP.
14. **Delivery hygiene:** closing an audit/ADR gap in the working tree without commit+push the same day is a process failure (dirty &gt; remote). See [`robustness-dx-debt.md`](robustness-dx-debt.md) “Critical delivery risks”.
15. Leaf CI (`dotnet test`) runs on push/PR; Bedrock E2E is still human / beta-hard — do not treat green unit CI as join/place/chest proof.
16. Protocol smoke bot (ADR §58): separate repo [`zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) (Bun + `bedrock-protocol`) — not mixed into the C# tree; offline join first; does not replace Gate A human client.
17. Reconnect playerdata (ADR §60): world LevelDB `pd:{uuid}` pose + GameMode; reserve Mojang `player_*` keys; no `players/` volume.
18. Dual storage (ADR §61): ZLDB default; Mojang worlds via `IChunkStorage` backend + offline converter — never mix schemas or silently reinterpret paths.
19. World domain (ADR §62/§71): `World` façade owns default **Overworld `Dimension`** (`ITerrainProvider` + wire id); `WorldStorageKeys` for KV prefixes — BDS/gen plug in without rewriting overlays. Nether/End Dimensions deferred.
20. Terrain gen (ADR §63–§67/§71/§72): `world.terrain: flat | noise` applies to overworld Dimension. Noise = **FastNoiseLite** (vendored Auburn MIT) OpenSimplex2 FBm height + **continuous** climate bias (no per-block hash), worm caves, ore, trees, ruins. Join/respawn = `SampleSpawnFeetY`; StartGame biome from `SampleSpawnBiome`. Existing `c:` blobs override config. Column `maxWorldY` includes cross-chunk tree canopy. Continuity: adjacent surface |ΔY| ≤ `MaxAdjacentSurfaceStep` (6).
21. Join terrain contract (ADR §70): pose heal → registries → **embedded** BiomeDefinitionList → PreSpawn ready-disk (`world.spawn-ready-radius`, default 2) → inventory/teleport → `PLAYER_SPAWN` → ChunkStream while `IsSpawning` fills the view ring. Ready-disk gen budget ≈ Measured `Noise_GetRadiusAsync` radius **2** (not full `spawn-chunk-radius`).
22. **Sparse world IO (ADR §45):** base terrain stays RAM on miss (no identical `c:` Put); disk writes are overlays / dirty blobs / inventory / playerdata. Prefer “IO only when state diverges from gen,” not “materialize every column.”
23. **CreativeContent wire (ADR §31/§52):** one send at join (all modes); remint on every `/gamemode` change (PM-shaped). Survival join + later Creative switch ⇒ two lifetime sends is normal.
24. **Yes-next after H1:** see [`roadmap.md`](roadmap.md) — §73–§74 survival drop/dig honesty shipped; optional next is floor-drop despawn TTL or smoke-bot `first10`; §61 Mojang only when import is the goal.
```

### Protocol smoke bot

See [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) (sibling checkout). Requires Bun and a running Zenith with `offline` in `auth.accept`. Keep JS/TS out of the Zenith server repo.

First-wave scripts: `bun run smoke:first10` (join → place → break → inv-hotbar → respawn → chest-open → double-chest → dig-timing → two-client). Persist (`smoke:persist`) needs LevelDB `world.path` and optional `ZENITH_PROJECT` for auto-restart.

Wave-2 extremes: `bun run smoke:wave2` (peer-ground → … → gravity → reconnect-pose). Opt-in persist with `ZENITH_SMOKE_INCLUDE_PERSIST=1` + `ZENITH_PROJECT`.

### Adding block/item capabilities (ADR §55)

**Glossary (Mojang-aligned):**

| Name | Means |
|------|--------|
| `BlockRuntimeId` | Block palette id (world cell / UpdateBlock) |
| `ItemNetworkId` | Item palette id (wire item) |
| `StackNetworkId` | ISR per-slot prediction id |
| `EntityRuntimeId` | Actor/entity id |
| `DestroySpeed` | Dig hardness (= dump `destroy_speed`) |
| `CreativeNetId` | CreativeCatalog remint id only |

**Add a Survival placeable + diggable block:**

```text
1. Name exists in block palette dump
2. Blocks.* const if hot path needs it
3. IsPlaceable allowlist (= PlaceAllowlist; no second set)
4. DigProfiles.Register(rid, DestroySpeed, harvest, effective, requiresCorrectTool)
   (tests: DigProfiles.OverrideForTests — scoped, no full-table reset)
5. Leaf test: BreakTicks (and place reject if needed); no Packets / no class BlockFoo
```

**Add a tool:**

```text
1. Name in ItemPalette
2. Tools.Register path (Tools = ToolProfiles façade — kind + tier)
3. CreativeCatalog Item StackId
4. Test: StackKind.Item + ToNetworkStack BlockRuntimeId=0
```

**Recipes:** exact `StackId` match (no facing merge). Block recipes use `StackKind.Block` only.

**Missing DigProfiles** = Survival cannot dig that rid (reject). Not the same as an explicit unbreakable profile later.

**Persistence:** `SlotBlob` v2 on write; v1 `inv:`/`ct:` migrate-on-read (`Tools.IsTool` → Item).

**Double-chest (ADR §56):** pair = adjacent same-facing cells; UI 54 = concat of two `ct:` halves; sneak+click chest places on face (no open).

**Add a new capability axis (e.g. food):** short ADR → `World/*Profiles.cs` sparse map → intent + system → Protocol transmits only. Do **not** invent `IBlockBehavior` or a plugin registry.

### Bedrock references (local clones)

Sibling tree (not in Zenith repo): `~/Development/references/bedrock/` — `bedrock-protocol-docs` (`r/26_u4`), `pocketmine-mp`, `dragonfly`, `serenityjs`, `endstone`, `powernukkitx`, **`Vedrock`**, **`vlang-leveldb`** (zlib Bedrock-shaped), **`goleveldb-mcpe`** (`df-mc/goleveldb`), `gophertunnel`, `bedrock-v/protocol`, and **`endstone-bedrock-protocol`** (`EndstoneMC/bedrock-protocol` — ADR §82). Use for wire/world/storage study; do not vendor into the C# tree. Dimension envelope: SerenityJS / Dragonfly. Overworld noise leaf: vendored Auburn **FastNoiseLite** (ADR §72); composition algorithms inspired by PocketMine `Normal`.

**Cereal-era (protocol 2168+) packet shapes specifically:** prefer `endstone-bedrock-protocol`'s `protocol/*.py` over the others. gophertunnel / `bedrock-v/protocol` / Endstone's C++ headers are all currently at **2168**, meaning for anything on the §79 Cereal-migration list their code still shows the shape being *replaced*, not the new one — a real trap, hit once already (§81). `endstone-bedrock-protocol` versions its schema explicitly (`@type(until=2168)` / `@type(since=2168)`, covering 975/1001/2168/2181 in one file), so the delta reads directly off the source instead of being inferred from a single snapshot. Its `CLAUDE.md` is worth reading before trusting any one field — it documents which source wins for which aspect (names vs. wire shape vs. golden bytes) and a list of settled disagreements.

**How to use refs when implementing a leaf (later checklist):**

| Source | Prefer for | Avoid copying |
|--------|------------|----------------|
| **Dragonfly** | Minimal / “essentials-only” shapes (break timing, session send, world seams) — good default cross-check | Treating DF as a full SMP product checklist |
| **PocketMine issues + changelogs** | What actually bites operators: tiny wire/DX gaps that look “bobo” but matter (recipe unregister, creative remint, dig edge cases, …) | PM’s “override everything” plugin surface (block/entity/item/tile god-hooks) as Zenith architecture |
| **PocketMine 5/6 command API** | Study typed `execute*(sender, …)` / arg-binding **ideas** when `/` framework is unfrozen | Shipping a command framework now (still freeze — single `/gamemode` is §52) |
| **Serenity / Endstone** | Protocol envelopes, Dimension-ish packaging | ECS/traits stacks or plugin loaders |

Before a product leaf: skim DF for the smallest honest path, then skim **open/closed PM issues** for that feature area so Zenith does not rediscover softcore footguns. Still: decide≠transmit≠serialize; no `Network/` revival; no plugin API until domains force an ADR.

### Bedrock protocol docs (official)

Clone (not in-repo): [`Mojang/bedrock-protocol-docs`](https://github.com/Mojang/bedrock-protocol-docs) branch **`r/26_u4`** → typically `~/Development/references/bedrock/bedrock-protocol-docs/`.

| Source | Role for Zenith |
|--------|-----------------|
| `ServerIdentity.ProtocolVersion` (**2169**) / `VersionName` (**1.26.50**) | What we announce today (ADR §79) |
| Docs JSON `x-protocol-version` on `r/26_u4` (**2169** / **1.26.50**) | Current target — matches `ServerIdentity` since the §79 bump |
| `changelog_2168_07_07_26.md` | What changed to reach 2169 — **~23 packets moved to Cereal serialization, not backwards compatible.** Zenith has **not** re-implemented those yet (§79 tracked debt); everything else on this branch is safe to encode against |
| PocketMine `BedrockProtocol` / Endstone BDS headers (older, pre-Cereal era) | Wire cross-check for the ~23 debt packets until they're migrated — these still reflect the pre-Cereal shape Zenith currently encodes |

**Rules:** Quiet-ACK unknown client→server noise (no WARNING) ≠ partial product. New **product** packets: full Encode/Decode + tests against `r/26_u4` (2169) — **except** the §79 Cereal-debt list, which stays pre-Cereal shaped until migrated (don't half-migrate one packet's fields to Cereal without doing the tagged-variant/optional-presence-byte encoding correctly — see ADR §79 non-goals).

Manual smoke expectations (clients A/B, terrain hashes, chat, place/break) live in [`ARCHITECTURE.md`](../ARCHITECTURE.md).

## IDE / tooling

- **.NET 10** solution (`zenith.sln`) with focused test projects
- Nullable + modern C# patterns (`ref struct` streams, `ValueTask` storage)
- YAML for ops config; future player-facing strings planned as TOML (`lang/*.toml`) — not mixed into `zenith.yml`
- VS Code: `.vscode/launch.json` — **Zenith** / **Zenith (no build)** with `cwd` = `src/zenith/bin/Debug/net10.0` (config beside DLL, ADR §20)

## Extending later (shape, not API yet)

When extension points open, prefer:

```
Gameplay domains  →  stable intents / events
                 ↘ Protocol (already decided sends)
```

Avoid:

```
Plugin → raw packet decode → mutate World on RakNet thread
```

That antipattern is how Bedrock stacks become un-upgradable across protocol bumps.

## Measured (optional)

Wire/hot-path baseline dated **2026-07-15** (Windows 11 / Ryzen 5 3600). **Worldgen** rows dated **2026-07-21** (Linux Fedora / i7-8650U / .NET 10.0.9) — ShortRun (`-j short -m --join`). Refresh with:

```bash
dotnet run -c Release --project src/zenith.Benchmarks -- -f * -j short -m --join
```

**Worldgen / PreSpawn join budget** (ADR §63–§69) — run alone (radius 4 is multi-second):

```bash
dotnet run -c Release --project src/zenith.Benchmarks -- -f *Worldgen* -j short -m --join
```

Hot-path suite (serialize + RAM decide). ShortRun margins are wide; treat as order-of-magnitude / alloc signal, **not** a CI gate (ADR §24).

| Area | Method | Params | Mean | Allocated |
|------|--------|--------|------|-----------|
| BinaryStream | WriteVarIntsAndInts (64 pairs) | — | ~498 ns | 1056 B |
| BinaryStream | ReadVarIntsAndInts (64 pairs) | — | ~152 ns | 0 B |
| GamePacket | Encode 3× PlayStatus `NONE` | — | ~134 ns | 440 B |
| GamePacket | Encode 2× UpdateBlock ZLIB under threshold | — | ~131 ns | 352 B |
| GamePacket | Encode 32× UpdateBlock ZLIB **over** threshold | — | ~6.0 µs | 4120 B |
| UpdateBlock batch | EncodeNone | 1 / 8 / 32 | ~80 / ~367 / ~1.3 µs | 264 / 1032 / 3712 B |
| UpdateBlock batch | EncodeZlibPref | 1 / 8 / 32 | ~82 / ~361 / ~5.9 µs | 264 / 1032 / 4136 B |
| LevelChunk | EncodeFlatColumn | — | ~144 ns | 1216 B |
| LevelChunk | EncodeNoiseColumn | — | ~591 ns | ~8.7 KB |
| Worldgen | Flat_BuildOverworldColumn | — | ~30 µs | ~5.2 KB |
| Worldgen | Noise_GetBaseColumn | — | ~30 ms | ~225 KB |
| Worldgen | Noise_CaveContextOnly | — | ~132 µs | ~190 KB |
| Worldgen | Encode_FlatLevelChunk / Encode_NoiseLevelChunk | — | ~135 ns / ~591 ns | ~1.2 KB / ~8.7 KB |
| Worldgen | Noise_GetRadiusAsync | radius 2 / 4 | ~305 ms / ~1.14 s | ~5.4 MB / ~18 MB |
| Inventory wire | EncodeInventoryContent36 | — | ~1.1 µs | 1056 B |
| Inventory wire | EncodeItemStackResponseOk | — | ~161 ns | 88 B |
| Inventory wire | EncodeItemStackResponseError | — | ~34 ns | 88 B |
| World overlay | SetBlock | overlays 0 / 1k / 10k | ~140 ns / ~7.4 µs / ~15 µs | 0 B |
| World overlay | GetBlock | overlays 0 / 1k / 10k | ~6–7 ns | 0 B |
| World overlay | GetOverlaysInColumn | overlays 0 / 1k / 10k | ~44 ns / ~6.2 µs / ~255 µs | 0 / ~33 KB / ~525 KB |

**Phase 3 note (2026-07-18):** hot pack leaves landed — `FillOverlaysInColumn` / `ForEachOverlayInColumn` (ColumnSend reuses scratch), `WriteVarString` stackalloc/ArrayPool, AuthInput bitset into `stackalloc`, FloorDrop delay keys via reused list. Refresh ShortRun numbers when convenient; prior `GetOverlaysInColumn` alloc row is the pre-Phase‑3 signal.

**Worldgen note (2026-07-21):** `WorldgenColumnBenchmarks` + `WorldgenPreSpawnBenchmarks` lock the §69 join budget. ShortRun on this laptop: **noise column ≈ 30 ms / ~225 KB**; **PreSpawn radius 4 ≈ 1.1 s / ~18 MB** (81 columns, parallel). Encode ≪ gen (~0.6 µs noise LevelChunk). Cave CSR alone ≈ 132 µs — payload fill dominates. Do not baseline against `FlatTerrainProvider.Instance` (cached singleton → ~0 ns); use `ChunkPayloads.BuildFlatOverworld`.

**Join contract note (ADR §70):** blocking PreSpawn uses `spawn-ready-radius` (default **2** → ~25 columns / Measured radius-2 row above). Full view ring streams after `PLAYER_SPAWN` while `IsSpawning`.

**Improvement signals from this run:** ZLIB Deflate on 32-block batches (~6 µs) dwarfs uncompressed encode; `GetOverlaysInColumn` allocated and scaled poorly at 10k overlays (~525 KB / ~255 µs) — addressed for stream path via fill/callback (§54). Palette `Blocks.*` is already a field hit; item name lookup stays cheap.

## Related

- [Comparison](comparison.md) — when Zenith DX wins vs plugin ecosystems  
- [Why Zenith](why-zenith.md) — long-term bet

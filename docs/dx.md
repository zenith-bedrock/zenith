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

`zenith.yml` next to the executable — same story in Visual Studio, `dotnet run`, or a Windows service. No hidden `ZENITH_*` matrix. Log levels are split: `log.server` (default `info`) vs `log.raknet` (default `warn`) so enabling Bedrock Debug does not flood ACK/`Connected PID`.

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
19. World domain (ADR §62): `World` façade; `ITerrainProvider` for base columns; `WorldStorageKeys` for KV prefixes — BDS/gen plug in without rewriting overlays.
20. Terrain gen (ADR §63/§64): `world.terrain: flat | noise`. Noise = overworld band (~Y 40–88), sea 62, caves, oak trees, cobble ruins, bedrock floor. Join/respawn = clear air via `SampleSpawnFeetY`. Existing `c:` blobs override config.
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

Sibling tree (not in Zenith repo): `~/Development/references/bedrock/` — `bedrock-protocol-docs` (`r/26_u4`), `pocketmine-mp`, `dragonfly`, `endstone`, `powernukkitx`, **`Vedrock`**, **`vlang-leveldb`** (zlib Bedrock-shaped), **`goleveldb-mcpe`** (`df-mc/goleveldb`). Use for wire/world/storage study; do not vendor into the C# tree.

### Bedrock protocol docs (official)

Clone (not in-repo): [`Mojang/bedrock-protocol-docs`](https://github.com/Mojang/bedrock-protocol-docs) branch **`r/26_u4`** → typically `~/Development/references/bedrock/bedrock-protocol-docs/`.

| Source | Role for Zenith |
|--------|-----------------|
| `ServerIdentity.ProtocolVersion` (**1001**) / `VersionName` (**1.26.33**) | What we encode today |
| Docs JSON `x-protocol-version` on `r/26_u4` (**2169** / **1.26.50**) | Newer train — shapes useful, **bit indices / new fields may shift** |
| `previous_changelogs/changelog_1001_*.md` | What changed at 1001 |
| PocketMine `BedrockProtocol` @ 1001 / Endstone BDS headers | Wire cross-check when docs are ahead of our protocol |

**Rules:** Quiet-ACK unknown client→server noise (no WARNING) ≠ partial product. New **product** packets: full Encode/Decode + tests against the protocol we speak (1001), not blindly against 2169 enums. AuthInput flag indices: keep Endstone/PM 1001 (`Sneaking=8`, …) until we bump protocol.

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

Baseline dated **2026-07-15**, host Windows 11 / .NET 10.0.10 / Ryzen 5 3600 — ShortRun (`-j short -m --join`). Refresh with:

```bash
dotnet run -c Release --project src/zenith.Benchmarks -- -f * -j short -m --join
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
| Inventory wire | EncodeInventoryContent36 | — | ~1.1 µs | 1056 B |
| Inventory wire | EncodeItemStackResponseOk | — | ~161 ns | 88 B |
| Inventory wire | EncodeItemStackResponseError | — | ~34 ns | 88 B |
| World overlay | SetBlock | overlays 0 / 1k / 10k | ~140 ns / ~7.4 µs / ~15 µs | 0 B |
| World overlay | GetBlock | overlays 0 / 1k / 10k | ~6–7 ns | 0 B |
| World overlay | GetOverlaysInColumn | overlays 0 / 1k / 10k | ~44 ns / ~6.2 µs / ~255 µs | 0 / ~33 KB / ~525 KB |

**Phase 3 note (2026-07-18):** hot pack leaves landed — `FillOverlaysInColumn` / `ForEachOverlayInColumn` (ColumnSend reuses scratch), `WriteVarString` stackalloc/ArrayPool, AuthInput bitset into `stackalloc`, FloorDrop delay keys via reused list. Refresh ShortRun numbers when convenient; prior `GetOverlaysInColumn` alloc row is the pre-Phase‑3 signal.

**Improvement signals from this run:** ZLIB Deflate on 32-block batches (~6 µs) dwarfs uncompressed encode; `GetOverlaysInColumn` allocated and scaled poorly at 10k overlays (~525 KB / ~255 µs) — addressed for stream path via fill/callback (§54). Palette `Blocks.*` is already a field hit; item name lookup stays cheap.

## Related

- [Comparison](comparison.md) — when Zenith DX wins vs plugin ecosystems  
- [Why Zenith](why-zenith.md) — long-term bet

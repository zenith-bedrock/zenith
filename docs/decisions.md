# Decision history

This log reconstructs **why** Zenith looks the way it does, using the git timeline (Dec 2024 → Jul 2026) and the living rules in [`ARCHITECTURE.md`](../ARCHITECTURE.md). Newest themes first within each era.

## Timeline (milestones)

| Period | Commits (indicative) | Theme |
|--------|----------------------|--------|
| Dec 2024 | `5064e74` → `881183b` | RakNet foundation: BinaryStream, FrameSet, login / StartGame plumbing |
| Jul 2026 | `1dd0501` → `ea6b6f1` | Session extraction, protocol **1.26.33** alignment, compression |
| Jul 2026 | `d63e7b2`, `548b289` | `BinaryStream` as **ref struct**; Packets vs Protocol split; GameLoop |
| Jul 2026 | `496cbb5` → `33ba520` | Visibility, chat, world read path, rate limits |
| Jul 2026 | `6cdc494` → `78950da` | `zenith.yml`, flat terrain, inventory/blocks |
| Jul 2026 | `2b1eed9` (+ follow-ups) | Owned **Zenith.Nbt** + **Zenith.LevelDB**, block palette, hash flag fix |

## Decisions worth spelling out

### 1. Own the stack from UDP up — don't wrap a PHP/Java runtime

**Choice:** C# / .NET with in-repo `raknet`, then game code on top.

**Why:** Bedrock is a binary protocol problem first. Controlling the transport and decode path (`BinaryStream`, FrameSet, reliability) avoids fighting an alien GC and FFI story for hot paths. Commits from late 2024 are almost entirely RakNet until login works — that order was deliberate.

### 2. `BinaryStream` as `ref struct`

**Choice:** Migrate readers/writers to `ref struct` and thread `ref` through Decode APIs (`d63e7b2`).

**Why:** Hot packet paths allocate less and make lifetime mistakes visible at compile time. Cost: API churn once — paid early so every later packet inherits the discipline.

### 3. Separate Packets, Protocol, Handlers, Gameplay

**Choice:** Explicit layering (`548b289` and `ARCHITECTURE.md`).

**Why:** PocketMine-style codebases often blur “decode packet” / “mutate world” / “send reply” in one listener. Zenith's rule set forces:

- Handlers write **intent**
- Systems mutate **domain**
- Protocols **send**

That makes tick order and backpressure mental models simpler and keeps wire formats testable without a full server boot.

### 4. GameLoop + pending input over Actor / ECS / Scheduler

**Choice:** Single-threaded 20 TPS loop; freeze Actor/ECS/DI/plugin frameworks until a feature forces them.

**Why:** Bedrock servers die from accidental architecture, not from missing an actor library. Intent queues + ordered systems match how movement and block place already work. Reintroducing ECS “because industry” would dilute clarity before product need.

### 5. Overlay persistence instead of column copy-on-write

**Choice:** Permanent sparse `ov:` keys (+ RAM map); send `UpdateBlock` after base `LevelChunk`.

**Why:** Early worlds are flat + edits. Rewriting subchunk palettes on every place is expensive and couples storage to encode. Overlays scale poorly forever (documented unbounded RAM map), but they ship correct multiplayer edit visibility **now** without a storage redesign.

### 6. Own NBT and LevelDB as leaf libraries

**Choice:** `Zenith.Nbt` (LE / Network / BigEndian) and managed `Zenith.LevelDB` — no NuGet native LevelDB, no Mojang world format decoder.

**Why:**

- Palette dumps are gzip + **BigEndian**; network property data is **Network** NBT — one wrong endian and terrain is air.
- Zenith keys (`c:`, `ov:`) only need a trustworthy KV, not vanilla world decode.
- Managed LevelDB removes native binding pain on Windows/.NET and keeps the dependency graph clean (same “leaf” idea as Nbt).

Trade-off: existing NuGet LevelDB dirs are not migrated — recreate world paths.

**Adendo (jul 2026 — dual storage / Mojang):** ZLDB remains the owned product store (§11). Opening or converting Mojang/BDS worlds is a **separate** backend + offline converter behind `IChunkStorage` — ADR §61. Do not decode vanilla worlds inside `Zenith.LevelDB`.

### 7. `UseBlockNetworkIdHashes = true` by default while using FNV palette IDs

**Choice:** Align StartGame with hash-based subchunk palettes (and spawn Y with flat spawn).

**Why:** Empirical: chunks were sent, clients still saw skybox. With the flag false, large FNV hashes were misread as legacy runtime IDs → everything air. Matching generators that “use blocks” (hash mode) fixed terrain without touching chunk send order.

### 8. Config beside the executable

**Choice:** `Path.Combine(AppContext.BaseDirectory, "zenith.yml")`.

**Why:** Relative `zenith.yml` followed the process cwd — broken for service hosts / debuggers / double-click launches. BaseDirectory matches “config next to the binary” mental model.

### 9. Auth accept modes (config visual)

**Choice:** `auth.accept` YAML list of named modes: `xbox` | `self-signed` | `offline`. Default LAN = all three + boot WARNING. Public = `[xbox]` only (strict chain checks). `offline` is parse fallbacks only — does not open AuthenticationType SELF_SIGNED. Obsolete `require-chain-signatures` aborts boot with actionable message (no auto-migrate / rewrite).

**Why:** Bool soft/hard mixed “who may join” with crypto. Ops need a readable multi-select. Loud fail on bad YAML matches gamemode / unmatched-key DX.

### 10. LGPL-3.0

**Choice:** License the tree under LGPL-3.0 (`d536987`).

**Why:** Keeps the core share-alike story while remaining practical for linking — a signal that Zenith is meant as infrastructure others build on, not a closed appliance.

### 11. Zenith.LevelDB = snapshot + WAL in RAM, not a full LSM

**Choice:** Collapse the early “mini-LSM” (mem + journal + merge iterator) into: **full dataset in a `SortedDictionary`**, WAL for durability, one fsynced snapshot named by `CURRENT`. Public API (`DB` / `WriteBatch` / `Iterator`) unchanged.

**Why:** Callers only need trustworthy `c:` / `ov:` KV in one process — not Mojang decode or RocksDB-scale compaction. Steady-state already rewrote a single table; adding L0/compaction would be storage-engine risk without a proven product need (`ARCHITECTURE.md` rule 7).

**Explicit constraint:** the **entire dataset must fit in RAM** while open. Documented in [`libs/leveldb/README.md`](../libs/leveldb/README.md). Crash mid-flush (new `.ldb`, old `CURRENT`) recovers by trusting `CURRENT` only; orphans are GC’d.

### 12. ItemPalette via ServerContext (not Items.Load static)

**Choice:** Load full `item_palette.json` into an immutable `ItemPalette` held on `ServerContext`. Protocols map block runtime → wire item id using `Blocks.TryGetName` + palette. Send `ItemRegistryPacket` (0xa2) after StartGame. Packets hold only `NetworkItemStack` DTOs.

**Why:** Clients need the full registry; hardcoded IDs lied about air (`0` vs `-158`). Injecting the palette keeps dependencies visible — **do not** grow another `Blocks.Load`-style static for items/biomes/mobs.

**Known debt:** `Blocks.*` remains a static façade for gameplay consts (`Blocks.Stone`, etc.). Next registry goes through Context; migrate Blocks when inventory/block domain is next touched.

**Fallback:** boot `Require` air/stone/grass (throw if missing); runtime unknown block → air item id + Warning capped at 64 distinct ids (never fake stone).

**Version SSOT:** `ServerIdentity.VersionName` drives StartGame / ResourcePackStack strings. ItemRegistry wire layout is documented against `ServerIdentity.ProtocolVersion`.

**Deferred:** full vanilla `creative_items.json` / `block_state_b64` parse (file remains embedded, intentionally unused — wire CreativeContent comes from `CreativeCatalog` only); behavioral item classes.

**Adendo (jul 2026 — registry maturity):** Dump-driven stance: full palettes for wire fidelity; curated `Blocks` façade for gameplay; creative/recipes stay separate SSOTs. `BlockPalette` records every `network_id → name` at NBT parse (`TryGetName`); `Blocks.TryGetName` uses curated map then palette reverse so Protocol name→ItemPalette bridge does not air-fallback for in-dump states outside the starter set. `Blocks.IsPlaceable` allowlists curated placeables (+ chest facings); handler + `BlockSystem` reject others. Still **not** dual block-id registries, a typed mega-registry, or Blocks→Context (§25).

### 13. GameLoop never waits on LevelDB overlay Put

**Choice:** `World.SetBlock` updates the in-RAM overlay map synchronously, then enqueues persistence (`LevelDbChunkStorage` overlay write queue). No `GetResult` / `.Wait()` on the tick thread.

**Why:** Overlay keys are idempotent (`ov:x:y:z`). Blocking 20 TPS on disk + the shared LevelDB `_gate` stalls every player for one builder's edits — opposite of the GameLoop “light and deterministic” rule.

**Satellites:** column `Get`/`Put` still share `_gate`. Crash mid-queue may lose unshed Puts until `Dispose` drains — best-effort flush on stop. PreSpawn no longer uses `GetResult` (see §14).

### 14. Flat chunk streaming + async PreSpawn

**Choice:** `PlayerChunkTracker` + `ChunkStreamSystem` stream missing columns around the player (async `GetOrCreateColumnAsync`, cap starts/tick). PreSpawn loads the spawn disk via `async` continuation (no receive-thread `GetResult`). Still flat base + overlays — not Mojang worlds / noise gen / VisibilitySystem.

**Adendo (jul 2026 — join overlay catch-up):** PreSpawn `RememberMany` right after LevelChunks (before T0 overlay loop). Live `UpdateBlock` fan-out reaches peers with `Chunks.Knows(cx,cz)` even when `!IsInGame`. On InGame enable set `NeedsOverlayResync`; first `ChunkStreamSystem` tick re-emits current overlays for known columns (`ColumnSend.EmitOverlaysToSession`) so places during the LevelChunk batch window are not lost.

**Adendo (jul 2026 — noise join timeout):** `world.terrain=noise` PreSpawn at default `spawn-chunk-radius: 4` generates **81** full columns on first join. Sequential gen was ~54s → client/RakNet timeout while stuck on “waiting for chunk radius”. Fix: **`World.GetRadiusAsync` parallel** (`Parallel.ForEachAsync`, core count) + swallow **`CLIENT_CACHE_STATUS`** in PreSpawn + INFO logs for load/publish. Smoke bot (`smoke:join`) confirms spawn &lt; ~15s on noise after fix.

**Adendo (jul 2026 — PreSpawn center + publish throttle):** PreSpawn loads / publisher / `PublisherCenterChanged` use the **player’s chunk** (from feet XYZ), not hardcoded `(0,0)`. `StartGame` spawn block matches feet. Publish uses **`LevelChunkBatchSize = 1`** (one LevelChunk per GamePacket) + `Task.Yield` between envelopes so noise payloads do not stall the reliable-ordered queue; INFO log reports column count, envelope count, approx payload bytes, and publish ms before `PlayStatus(PLAYER_SPAWN)`.

**Adendo (jul 2026 — join stuck / PM-shaped spawn):** PocketMine sends a **spawn threshold** of chunks then `PLAYER_SPAWN`, streaming the rest; inventory is seeded in PreSpawn. Zenith: (1) **spawn heal** — if saved feet are not clear air (classic flat `pd:` Y=-60 into `terrain=noise`), snap to `SampleSpawnFeetY` + Persist; (2) **ready-disk** before `PLAYER_SPAWN` (superseded by ADR §70 `world.spawn-ready-radius`); (3) inventory + MovePlayer teleport before `PlayStatus`. Fixes client stuck on loading when buried in solid noise.

**Why:** Closes the “world dies outside spawn radius” gap without Actor/EventHandlers or vanilla LevelDB. Matches early Zenith spine: continuous flat multiplayer before effects/gen.

**Deferred (conscious):** food/effects, creative inventory, biomes/noise, Actor/EventHandler frameworks (not mirroring early “everything is an Actor” stacks).

### 15. Block authority harden + quiet Animate / LevelSoundEvent

**Choice:** Keep Handler → FIFO `BlockEditIntent` → `BlockSystem` for place/break. On tick: re-check bounds, Euclidean reach from **eyes** (`PositionY + PlayerEyeHeight`) with `Player.MaxBlockReach = 6`, place only into air (consume hotbar **after** that check), break only non-air and only if `TryAdd` succeeds (never void items). In-game ACK Animate (`0x2c`) and LevelSoundEvent (`0x7b`) by packet id without DTO fan-out. `MobEquipment` Decode updates `SelectedHotbarSlot` only (place still uses baked intent slot).

**Why:** Closes Fase 3 gaps (occupied place / full-hotbar break / unreachable edits) without Actor or domain EventHandlers. Log spam from swing/sound was WARNING noise, not missing gameplay. Reach is intentionally simple (not AABB/physics).

**Deferred:** AuthInput block path, sound peer fan-out, food/effects. (Inventory storage closed in §16. Animate peer fan-out → §53.)

**Adendo (jul 2026 — chat FIFO §54):** `SubmitChat` is a capped FIFO (`MaxPendingChat = 8`, like block edits) — overflow rejects the newest. `ChatSystem` drains all pending messages per player per tick (not overwrite-latest).

**Adendo (jul 2026 — break path):** Survival destroy arrives via `PlayerAuthInputPacket.BlockActions` when `ServerAuthoritativeBlockBreaking=true` in StartGame. Decode past the position prefix (bitset + flags → `PlayerBlockAction`); map `predict_destroy` (26) → `TrySubmitBreak` / `BlockEditIntent` air. Keep `PlayerAction` (13/26) and `InventoryTransaction` UseDestroy as fallbacks. Progress actions (`start_break` / `crack_break` / `continue_destroy`) ignored until §27.

**Adendo (jul 2026 — place vs player AABB):** After air + placeable checks, before `TryConsumeOne`, reject place when the full-cell block AABB intersects the placer or any other InGame player's standing AABB (inset `1e-4` to avoid flush-face false positives). Reuses `Geometry/Aabb` + `EntityHitboxes` (no Entity/ECS). Reject → `ResyncCellToBreaker` only (no neighbor fan-out — avoids self-place sync glitches). Floor drops are not colliders. Creative uses the same rule. **Known debt:** fixed standing BB (no sneak/swim); full-cube cells only until slab models; jump-place while BB still overlaps stays reject (no separate physics). **Smoke:** place into body → reject / no item loss / no stuck; flush adjacent → OK; A in cell + B places → B rejected.

### 16. Main inventory 36 slots; place stays hotbar-only

**Choice:** `PlayerInventory` owns one `_slots[36]` array (`FullInventorySize`). Hotbar helpers remain `IsValidHotbarSlot` 0–8 for place / MobEquipment / `TryConsumeOne`. `IsValidInventorySlot` 0–35 for Get/TrySet/TryAdd. `TryAdd` stacks then fills across the full window. Rename `SendHotbarContent` → `SendInventoryContent` (already sent window 0 length 36). No InventorySystem, CreativeContent, deep InventoryTransaction place, Actor, or effects.

**Why:** After §15, break aborted when hotbar was full even though the client UI had empty storage slots 9–35 — product gap on the Fase 3 path. Expanding storage without widening place intents avoids half-protocol race with open-inventory TX.

**Deferred:** floor drops, CreativeContent, food/effects, Actor/EventHandlers. (Click-move closed in §17.)

### 17. Player inventory rearrange via ItemStackRequest (tick authority)

**Choice:** Enable `ServerAuthoritativeInventory` in StartGame. Client moves use `ItemStackRequest` (take/place/swap) → handler maps containers 12/28/29/cursor → flat → FIFO `InventoryStackIntent` → `InventorySystem` applies `TryTransfer`/`TrySwap` (all-or-nothing snapshot) → same-session `ItemStackResponse`. Stack network IDs live in `InventoryProtocol` only. Place/break stay hotbar IT UseItem. Open inventory: Interact → `ContainerOpen` window 0.

**Why:** After §16, drag in the client UI desynced on the next `SendInventoryContent` because moves never reached domain authority. Adding ISR without chests keeps decide≠transmit: domain owns slots/cursor; Protocol owns net IDs and wire. `InventorySystem` exists because there is now intent + tick + transmit (unlike §16’s TryAdd-only expansion).

**Deferred:** creative craft, drop entity, destroy-void, AuthInput-embedded ISR, Normal IT slot moves. (Held peer → §18. Chests → §19 sketch.)

### 18. Held-item peer sync (MobEquipment fan-out)

**Choice:** Handler still only updates `SelectedHotbarSlot` (MobEquipment / IT). `EquipmentSystem` on tick compares held slot + stack fingerprint to last broadcast; if changed, fan-out `MobEquipment` Encode to other in-game peers (same pattern as `MovementSystem`). `AddPlayer` includes held `ItemInstance` (legacy wire) from current hotbar. Stack net IDs omitted on peer view (display only). No armor/offhand.

**Why:** After §17, A’s hotbar is authoritative but peers still saw empty hands — smoke 3–4 incomplete for “looks multiplayer.” Chests can wait; this is cheaper and closes a visible MP gap.

**Deferred:** chests/containers, inventory persist across reconnect, drop entity.

### 19. Chests — deferred sketch (shipped as §28 MVP)

When held sync + §17 smoke are stable: (sketch retained; **MVP landed in §28** — RAM store + container 7 + flat `100+` outside `PlayerInventory`; LevelDB `ct:` / double-chest still out.)

1. `ChestStore` RAM `dict[(x,y,z)] → slots[27]`; hydrate at World boot; Put `ct:x:y:z` async (never sync Get on open/tick).
2. Open/close intents; transfers via explicit dual-store APIs (not flat `100+` inside `PlayerInventory`); rollback snapshots both stores.
3. Wire: ContainerOpen type 0; ISR container **7** ↔ chest 0–26; window-scoped net ids.
4. Break: dump all-or-nothing `TryAdd` else abort.
5. Out: double-chest, Mojang BlockActor, hopper, forge, CreativeContent.

### 20. First release artifacts + on-disk layout (`v0.0.1-alpha`)

**Choice:** Ship a product version separate from Bedrock wire: `ServerIdentity.ProductVersion` (e.g. `"0.0.1-alpha"`). Docker: `WORKDIR /app`; host sample under `deploy/` — mount `./deploy/zenith.yml:/app/zenith.yml` and `./deploy/worlds:/app/worlds` (not used by `dotnet run`). On-disk world = `{world.path}/worlds/{world.name}/` LevelDB (path empty ⇒ InMemory). Sample compose uses `world.path: /app`, `world.name: world`. Multi-world load stays a non-goal; folder convention only.

**Why:** No published channel existed for external feedback (Vedrock already had Docker + tag). `zenith.yml` resolves via `AppContext.BaseDirectory` next to the DLL — config cannot live solely under a PMMP-style `/data` that replaces `/app`. A separate `players/` volume was deferred until playerdata existed.

**Adendo (jul 2026 — playerdata without `players/` volume):** Pose + GameMode now persist in the world LevelDB as `pd:{uuid}` (§60), same family as `inv:` / `ct:`. A separate `players/` mount remains Deferred — Mojang co-locates player NBT in the world DB; softcore `players/` trees diverge from that path.

**Known debt / principle:** Reinterpreting a former flat LevelDB directory as a data root creates `{path}/worlds/{name}/` empty while orphaning old `CURRENT`/`.ldb` at the root — silent empty world. Detect and **Warning** at boot if root looks like ZLDB and the new world dir is empty/new. Future path-semantics changes must warn loudly, never reinterpret silently (ops visibility vs silent fallback). Relative `world.path` resolves against `AppContext.BaseDirectory` (DLL dir), never process cwd — same as `zenith.yml`.

**Adendo (jul 2026 — Docker `/data` UX):** PocketMine/Endstone-style **one volume on `/data`**. Image sets `ZENITH_DATA=/data`; config = `/data/zenith.yml`. Entrypoint seeds default config on first boot. **`ZENITH_DATA` is the only supported env override** (data root — not a config matrix). Do not bind-mount over the binary dir (fragile on Dokploy when host file missing → directory). Config changes require container **restart** (read at boot only).

**Adendo (jul 2026 — unified image + ZENITH_DATA owns world):** One product image ([`deploy/image/`](../deploy/image/)) — binary at `/opt/zenith`, default `ZENITH_DATA=/data`. Platforms (Compose/Dokploy, Pterodactyl egg) are **adapters only** — they set `ZENITH_DATA`, mounts, and panel metadata; no second Dockerfile. When `world.path` is empty/omitted **and** `ZENITH_DATA` is set → LevelDB root = `ZENITH_DATA` (`…/worlds/<name>/`). Empty path without `ZENITH_DATA` stays InMemory (`dotnet run`). Explicit absolute `world.path` still wins. Single [`deploy/zenith.yml`](../deploy/zenith.yml) template for all hosts.

**Deferred:** Multi-world load, migrator flat→`worlds/<name>`, `players/` volume, Docker Hub automation.

### 21. Future extension surface form (not a schedule)

**Choice:** When (if) external extension opens, the first surface is **`EventBus.Subscribe<T>`** — already typed, exception-isolated, used for login/quit. Do **not** expose `GameLoop.Register` to foreign assemblies or hook `Protocol.Send*`.

**Why:** Keeps decide≠transmit; avoids early plugin loader / DI. `ChunkStreamSystem` / block+inventory systems stay internal composition-root concerns.

**Deferred:** Domain event types beyond login/quit; `Unsubscribe`; priority; real plugin API (still frozen).

### 22. Protocol churn process (human checklist)

**Choice:** Manual review on a trimestral cadence (or when targeting a new Bedrock client), documented in [`docs/protocol-churn.md`](protocol-churn.md). No scraper / decorative CI.

**Why:** Protocol SSOT already lives in `ServerIdentity`; the gap was process, not another code abstraction.

**Deferred:** Automated CI against remote protocol schemas.

### 23. Zenith.LevelDB robustness tests + flush fault hook

**Choice:** Keep ZLDB / RAM constraint. Add tests for mid-flush (orphan `.ldb`, `CURRENT` still old), missing/corrupt `CURRENT`, truncated WAL, and Close vs Put contract. Internal hook `AfterTableWriteBeforeCurrent` between table write and CURRENT publish for deterministic mid-flush simulation (`InternalsVisibleTo` already present).

**Why:** README already promised crash behavior; six happy-path tests did not prove it.

**Deferred:** WAL CRC framing; subprocess crash tests as primary CI mechanism.

### 24. Measured zero-alloc (BenchmarkDotNet)

**Choice:** Project `src/zenith.Benchmarks` measuring `BinaryStream`, a small `GamePacket` encode path, and `EventBus.Publish`. Package version in `Directory.Packages.props`. Numbers live only in `docs/dx.md` (“Measured”). Optional manual alloc probe — not a CI gate for alpha.

**Adendo (jul 2026 — worldgen suite):** `WorldgenColumnBenchmarks` (flat vs noise column, cave context, LevelChunk encode) + `WorldgenPreSpawnBenchmarks` (`GetRadiusAsync` radius 2 / 4). Filter: `-f *Worldgen*`. Join budget signal for ADR §69 — still optional ShortRun, not CI. Cave alloc leaf → §75.

**Why:** Wire zero-alloc work lacked numbers; avoids claiming DX without evidence.

**Deferred:** Full-server load harness as required CI.

### 25. Blocks façade stays static (known debt)

**Choice:** Keep `Blocks` as a static Load/EnsureLoaded façade over `block_palette.nbt` network ids, even after dirt/planks/log/sand/chest variety. Do **not** migrate to `ServerContext` in the same leva as chest/craft.

**Why:** Every hot path (`PlayerInventory` defaults, `BreakTicks`, wire name lookup) already calls the façade; injecting palette refs everywhere is a refactor without product gain while recipes/chest stabilize.

**Deferred:** Inject runtime-id table via `ServerContext` when a second palette / dimension forces it.

### 26. Floor drops without item entities

**Choice:** When break `TryAdd` fails, still break the block and deposit `(runtimeId,count)` in `World.FloorDrops` (sparse cell map). `BlockSystem` picks up within 1.5 blocks on tick via `TryAdd`. No Bedrock item entities, no physics.

**Why:** Full hotbar must not soft-lock survival break; entity actors stay frozen.

**Deferred:** Merge across cells, despawn timers, gravity/physics, LevelDB persist of drops. (Wire shipped — adendo below.)

**Known debt (updated §36):** SoftCap refuse on new cells (`2048`); merge into existing cells still allowed. Warn once at refuse.

**Adendo (jul 2026 — drop-entity wire MVP):** Each floor cell also holds `EntityRuntimeId` (from `PlayerManager.AllocateRuntimeId`). Spawn → `AddItemActor` (0x0f) to peers with `Chunks.Knows`; pickup → `TakeItemActor` (0x11). Merge count change → `RemoveActor` + `AddItemActor`. Late join / column stream catch-up emits Add for drops in known columns. Position = cell center, velocity zero (floating OK). Still **no** WorldEntity/ECS, gravity tick, despawn TTL, or LevelDB persist of drops (§32). **Q-throw + death loot → §73.**

**Adendo (jul 2026 — AddItemActor ItemStackWrapper):** Protocol 1001 encodes AddItemActor item as legacy **ItemStackWrapper** (same as AddPlayer held), not NetworkItemStackDescriptor. Wrong shape crashed the Bedrock client after full-inv floor drop. `NetworkItemStack` writers use Mojang wire names (`WriteItemStackWrapper` / `WriteNetworkItemStackDescriptor` / `WriteItemStack`); packet Encode picks; skip Add when `DescribeStack` is air.

**Adendo (jul 2026 — partial pickup):** `PickupFloorDrops` uses `TryAddUpTo` + `FloorDropStore.TryTakeUpTo`. Partial fit (e.g. stack 63 + floor count 5) takes what fits, `TakeItemActor`, then Remove+Add republish for remainder. `TryAdd` (break/chest dump) stays all-or-nothing with `CaptureSnapshot`/`RestoreSnapshot` so a failed full add never sticky-fills 63→64.

**Adendo (jul 2026 — pickup reach + break partial):** Pickup reach uses item-entity Y (`+0.125`, same as `AddItemActor`) and player torso (`feet + 0.75`), radius **2** blocks — old cell-center `+0.5` / 1.5 from feet failed rim-of-hole smoke. Break/chest dump uses `TryAddUpTo` + floor surplus (not all-or-nothing before drop). `Blocks.SameMergeItem` / `NormalizeMergeRuntimeId` stack chest facings and same-name palette rids. Roll back inventory if `TryTakeUpTo` fails after `TryAddUpTo`.

**Adendo (jul 2026 — AuthInput eye→feet):** StartGame / AuthInput wire Y is eye-space. Domain `Player.PositionY` is feet (`FlatSpawnY`, block reach `+ PlayerEyeHeight`). `InGameSessionHandler` submits via `MovementInputState.FromClientAuthInput` (`eyeY - PlayerEyeHeight`).

**Adendo (jul 2026 — Absolute network offset):** Bedrock player `MoveActorAbsolute` / local `MovePlayer` need wire Y = feet **+ 1.621** (`PlayerEyeHeight` + 0.001 so Absolute does not clip into the block top). `AddPlayer` stays at bare feet. Before AuthInput→feet, raw eye Y on Absolute accidentally looked correct; after feet domain, bare Absolute sank peers into the floor. `EntityProtocol` applies `EntityHitboxes.AbsoluteWireY`.

**Adendo (jul 2026 — AABB pickup + delay):** Sphere/`torso+0.75` retired. Pickup = player standing AABB (`0.6×1.8` feet) **expanded** `(1, 0.5, 1)` ∩ item AABB `0.25³` at cell (expand player side, not item). Pure math in `Geometry/Aabb`; Bedrock sizes in `World/EntityHitboxes`. `FloorDropStore` default **pickup delay 10** ticks (new cell); merge delay = `max(existing, incoming)`; partial `TryTakeUpTo` / republish **does not** reset delay. `TickPickupDelays` once per `BlockSystem` tick before reach checks — same-tick deposit becomes 10→9, not vacuum. Wire still shows item during delay. Expand `+0.5` Y does **not** reach an item one full block below feet (rim-of-hole) — step into the cell / same Y (vanilla-shaped, not the old sphere). **Known debt:** fixed standing BB (no sneak/swim); no off-hand preference; block interact reach stays Euclidean eyes (§15). Proximity gameplay going forward prefers AABB (orientation, not a mandate to rewrite §15 this PR).

### 27. Server-authoritative break timing

**Choice:** Soft blocks use `Blocks.BreakTicks` (empty-hand ≈ hardness×5s @ 20 TPS) snapshotted on AuthInput `start_break`. Same-cell `continue_destroy` does **not** reset the dig timer or re-send `StartCrack` (that finished the crack animation before `SetBlock`). Crack LevelEvent data = `65535 / ticks` (**truncating**, PocketMine/Dragonfly — not `Round`; rounding made crack finish one tick early for shovel-dirt / grass / stone-hand / etc.). Creative InstantBuild skips crack + timing gate. Early/wrong-cell breaks rejected with Debug log.

**Adendo (jul 2026 — dig desync):** Survival `predict_destroy` freezes dig auth (`DigStartedTick` / `DigRequiredTicks`) into `BlockEditIntent`, then `ClearBreakTarget` without StopCrack so same-AuthInput Continue can retarget. `BlockSystem` validates from the intent snapshot, not live `HasBreakTarget`. Reject (break/place) → self `UpdateBlock` of server truth to the breaker only. AuthInput break order: **Abort → Start/Crack → Predict → Continue** (cancel+redig needs Start before Predict; chain-break needs Predict before Continue). Abort always `StopCrack` at Abort packet coords (even when dig already cleared) and does **not** dequeue DigAuthorized intents (`predict` = commit). Same-cell MP: first DigAuthorized in tick order wins loot; loser sees air + Resync — no per-cell dig lock.

**Why:** Instant survival break was an authority hole after AuthInput destroy landed (§15 adendo). Restarting crack on every continue made the animation complete while the server still rejected `predict_destroy`. Live dig + Continue retarget before tick rejected the queued destroy → client ghost + peer desync.

**Deferred:** Efficiency enchant, haste/water/airborne dig modifiers, gold/netherite/copper tools, tool crafting recipes, durability. **Wrong-tool no-drop → §74.**

**Adendo (jul 2026 — crack vs dig gate):** Truncating `65535/ticks` alone was not enough when the client sends `predict_destroy` on the last mining tick (`elapsed == need-1` vs DigStartedTick from AuthInput start). Gate now accepts `elapsed >= need-1` (need&gt;1). `CrackEventData` still truncates like PM `(int)(65535/ticks)` / DF and **guarantees** `floor(65535/data) >= need` so Round-style overshoot cannot finish the crack UI early.

**Adendo (jul 2026 — tool dig speed):** Dig duration uses Dragonfly/wiki `BreakDuration` (no haste/water/Efficiency): `speed` from curated tool tier when Effective∧Harvestable else 1; `ticks = ceil(1/(speed/hardness/(30|100)))`. Curated tools: wood→diamond pick/axe/shovel (`World/Tools` + item network ids in inventory stack type). `World/BreakDuration` is SSOT; `Blocks.BreakTicks` delegates. AuthInput start snapshots held stack type + need. Mid-dig held change **preserves progress fraction** then retargets `DigStartedTick`/`DigRequiredTicks`; LevelEvent **3600** on dig start, **3602** only when `CrackEventData` changes (Mojang UpdateBlockCracking / DF ContinueCrack). Creative InstantBuild unchanged. Inventory wire: `Blocks.TryGetName` then `Tools.TryGetName` (tool `blockRuntimeId=0`). CreativeCatalog lists the 12 tools. Endstone digger components / ECS dig cache / BlockActor — **not** copied. Refs: DF `break_info.go`, Mojang `BlockBreakingOverview.md`.

**Adendo (jul 2026 — dig idle StopCrack):** `StartCrack` encodes full dig duration; if the client stops looking/digging without `AbortBreak`, peers kept seeing crack until the animation finished. Same-cell `crack_break`/`continue_destroy` now `MarkDigActive`; `BlockSystem.AbortIdleDigIfStale` stops crack after `DigIdleAbortTicks` without activity.

**Adendo (jul 2026 — crack flicker fix):** `DigIdleAbortTicks` was 10 (0.5s) — too short when clients send sparse `crack_break` while holding mine. Now **40** (~2s) + refresh `LastDigActivityTick` when AuthInput **`PerformBlockActions`** flag is set or any block action targets the current break cell. Stops crack only when the client stops reporting block interaction, not between progress packets.

**Adendo (jul 2026 — dig idle vs BreakRequiredTicks):** Hand-mining stone needs ~**150** ticks — still &gt; `DigIdleAbortTicks` (40). Clients often leave `PerformBlockActions` unset between sparse crack packets, so activity never refreshes; idle `StopCrack` + client `AbortBreak` cleared dig auth mid-swing → Predict rejected → `ResyncCell` + no floor loot. Soft blocks (dirt/grass) finish inside the idle window. `AbortIdleDigIfStale` now waits until `BreakStartedTick + BreakRequiredTicks` before applying the idle grace; `AbortBreak` also clears provisional dig auth. Activity refresh also uses AuthInput **MissedSwing** while `HasBreakTarget`. **Tradeoff:** abandon mid-hard-dig without `AbortBreak` may leave peer crack until dig window ends.

**Adendo (jul 2026 — dig lifecycle reject StopCrack):** DigAuthorized Predict still `ClearBreakTarget` + `CancelPendingDigStart` before tick (chain-break Continue). That left **zombie crack** when `ApplyEdit` rejected (early / no-auth / SoftCap / reach): Resync restored the block while LevelEvent 3600 kept playing. `RejectBreakToBreaker` = `StopCrack` + optional `AbortBreak` + `ResyncCell`. Same-tick Start+Predict with `need&gt;1` still rejects (`elapsed==0`); tests must not backdate the clock to hide that.

### 28. Chests — RAM store + ISR container 7 (MVP)

**Choice:** Ship the §19 sketch minimally:

1. `ChestStore` dict `(x,y,z) → InventorySlot[27]`; `Ensure` on place; `RemoveAndDump` on break (contents → `TryAdd` else floor drops). No LevelDB `ct:` keys yet.
2. `Player.OpenChest` nullable; set on empty-hand UseClickBlock on chest; clear on `ContainerClose`. Wire: `ContainerOpen` type **0**, window id **2** (≠ `0xff`); `InventoryContent` for chest + player.
3. Flat ISR slots `100+` map from container **7**; `PlayerInventory` does not own chest. `InventorySystem` dual-store transfer/swap with snapshot rollback of player + chest.
4. `PlaceInContainer` / `TakeOutContainer` decode as supported (same shape as take/place).

**Why:** Inventory rearrange (§17) without chests still left Survival “storage furniture” incomplete; dual-store keeps decide≠transmit without stuffing chest into `PlayerInventory`.

**Deferred:** LevelDB hydrate/Put; BlockActor; hopper; full creative item list / `block_state_b64`; window-scoped net-id isolation beyond protocol arrays. **Sneak-place + double-chest → §56.**

**Known debt (updated §36):** Warn once when Ensure count crosses `10_000` — still no refuse/eviction (needs LevelDB redesign).

**Adendo (jul 2026 — chest lid BlockEvent):** Lid animation is wire-only — `BlockEvent` (0x1a) `ChangeChestState` (type **1**), `EventData` **1** open / **0** close. No BlockActor NBT. `ChestStore` keeps opener refcount per cell (player runtime id set): animate open on **0→1**, close on **1→0**. `WorldProtocol.SendBlockEvent` is session-scoped; `ChestLidFanout` sends to opener + `IsInGame` peers with `Chunks.Knows` of the chest column (same Knows pattern as UpdateBlock joiners — not VisibilitySystem). Hooks: `InventorySystem` ApplyWindow OpenChest / Close; break clears openers + fans close if needed; disconnect / void-death release opener. Optional LevelSound later — not required for lid honesty.

### 29. Crafting 2×2 — RecipeRegistry MVP (wire completed in §35)

**Choice (updated Jul 2026):** Ephemeral `PlayerCraftUi` (grid flats `CraftUiBase`+0–3, result `CraftResultFlat`) outside `PlayerInventory` — chest-flat pattern. ISR maps containers 13/60; `CraftRecipe`+`Create`+`Consume` bake into one intent; tick `TryCraftFromGrid` then materialize result. Window **124** UI content on spawn + inventory open. Bag-only `TryCraft` remains for tools/tests. **`CraftRecipe.NumberOfCrafts` honored** (shift-click output): consume ×N, result `out×N` clamped by grid affordability and single-slot `MaxStack` (H0).

**Why:** Empty ISR OK / bag consume never drove SAI craft UI (S35). Grid + Create is enough without a full 54-slot UI inventory. Discarding times made shift-click Place N×out fail after craft ×1. Skipping CreatedOutput (60) in ItemStackResponse desynced sequential take after planks→chest — **fixed:** always emit container 60 on OK; refuse Survival craft while Result still occupied.

**Deferred:** 3×3 crafting table; recipe-book **CraftRecipeAuto**; container 14 preview sync; negative stack-net-id prediction; multi-stack CreatedOutput when out×N > MaxStack; double-click gather (no dedicated ISR opcode — client multi Place/Take, intermittent).

### 30. CreativeContent after this spine (conscious yes) — superseded by §31

**Choice (historical):** Prioritize Creative for the **next** feedback-facing milestone after place/break + variety smoke. Recording **yes-next** avoided another silent Deferred cycle.

**Status:** Shipped as §31 (Creative mode v1 + short CreativeContent list). Full `creative_items.json` / `block_state_b64` remains Deferred under §31.

### 31. Creative mode v1 (config → join)

**Choice:**

1. `ServerConfig.Validate()` accepts only `"Survival"` / `"Creative"` (loud fail; no silent fallback).
2. Every joiner gets that single mode from config: `Player.GameMode` plus both `StartGamePacket.GameMode` (PlayerGameMode) and `GameType` (WorldGameMode) from the same config value. Runtime Survival↔Creative is §52 (`/gamemode`); Adventure/Spectator still out.
3. Creative join: empty hotbar (client Creative UI supplies items). Survival: existing starter seed.
4. `CreativeContentPacket` after `ItemRegistry`, before CraftingData / BiomeDefinitionList — short list from `CreativeCatalog`. Do **not** parse `creative_items.json` (`block_state_b64`). Sent on **every** join regardless of Survival/Creative (catalog seed; UI gated by gamemode — same as PM/DF/Serenity).
5. `BlockSystem.ApplyEdit` branches: Creative place skips `TryConsumeOne`; Creative break skips timing + **block** inventory/floor loot (still clears chest store). **Chest contents → floor even in Creative (§74)** — never silent void of `ct:`. Survival path unchanged until §74 loot gate.

**Why:** LAN feedback needs Creative UI + infinite place/break without inventing entity wire or admin commands. Uniform config mode is the only source of truth while `/` stays frozen.

**Deferred:** Adventure/Spectator; pick-block; rich creative tabs; full creative list / `block_state_b64`; craft ISR quirks unique to Creative.

**Adendo (jul 2026):** Runtime `/gamemode` → §52 (overrides “no runtime switch” above for Survival↔Creative only).

### 32. Entity coherence — defer WorldEntity

**Choice:** Keep `FloorDropStore` / `ChestStore` without a shared entity concept. **Do not** introduce `WorldEntity` until the named feature **drop-entity wire** (§26 Deferred) is pulled as work. When that feature is pulled: minimal dropped-item entity only (position, runtime id, count, entity id for Add/Remove) — not ECS/mobs.

**Why:** Three RAM workarounds converging is Rule 7 signal, but Creative (§31) and current chests do **not** require entity wire. A generic entity layer “for mobs someday” violates decisions Item 4 / ARCHITECTURE Rule 7.

**Status (jul 2026):** Drop-entity **wire** shipped as §26 adendo — entity id lives on the floor-drop **cell**, not a `WorldEntity` type. Mob AI / persistence / collision remain Deferred.

**Deferred (after minimal dropped-item entity):** mob AI, Mojang BlockActor, entity persistence, collision — each its own ADR.

### 33. Store unbounded honesty + InventoryContent encode-shape

**Choice:** Document Known debt on §26 / §28 (unbounded dicts). Add an **encode-shape** test for outbound `InventoryContentPacket` in the style of existing inventory packet tests — do **not** implement `Decode` (packet remains outbound-only).

**Why:** ARCHITECTURE already documents unbounded `_blockOverrides`; chest/floor stores match that pattern and should say so. Round-trip wording would force useless Decode stubs.

### 34. HUD spawn honesty (seed, not authority)

**Choice:** After empty `BiomeDefinitionList`, send local `SetActorData` (FLAGS include `Breathing`) then `UpdateAttributes` with **frozen** defaults (health/hunger/saturation/exhaustion/movement/level/xp). Call site is spawn orchestration only — same pattern as CreativeContent. **No** `Player.Health`/`Hunger` fields, **no** `VitalsSystem`, **no** `UpdateAbilities`/`UpdateAdventureSettings` in this leva.

**Why:** Survival clients expect local metadata/attributes; omission leaves air/hunger HUD broken. Smallest honest surface (Rule 7); food/effects remain Deferred (§14–16). Domain vitals without tick authority would mirror pre-§17 inventory lie.

**Deferred:** VitalsSystem + Player vitals authority; water/`AirSupply`; hunger tick; food; damage. (UpdateAbilities/Adventure → §37.)

### 35. CraftingDataPacket remint (RecipeRegistry SSOT)

**Choice:** `CRAFTING_DATA_PACKET = 0x34` after `CreativeContent`, before empty `BiomeDefinitionList`. Protocol builds shapeless DTOs from `RecipeRegistry.SnapshotRecipes()` + `ItemPalette` + `Blocks.TryGetName` — **no** second `CreateDefault` recipe list in Packets. `ClearRecipes = true` intentionally replaces the vanilla book with Zenith’s two recipes only. Unlock AlwaysUnlocked; UUID zeros; block `"crafting_table"`; `RecipeNetworkID` = registry net id (1 / 2).

**Why:** Without remint, client 2×2 never sends our ISR net ids — registry craft stays dead on the wire. SSOT in Protocol avoids CreativeContent-style recipe table drift.

**Deferred:** Shaped grid, AutoCraft, full Mojang dump, 3×3 table.

### 36. Store honesty (warn + floor refuse-cap)

**Choice:**

1. **Overlays** (`World._blockOverrides`): SoftCap `10_000` on **new** keys (`TrySetBlock`); compact when rid == flat base; warn once at refuse. Hydrate bypasses SoftCap.
2. **ChestStore:** SoftCap `10_000` on **new** cells (`TryEnsure`); hydrate bypasses SoftCap.
3. **FloorDropStore:** SoftCap `2048` — `TryAddOrMerge` refuses **new** cell keys at cap + Warning once; merging into an existing same-item cell still succeeds.

**Why:** Rule 7 — unbounded overlay/chest RAM under grief is a production DoS, not “known debt to ignore.” Compact-to-base keeps honest dig of restored sky cells without inventing eviction I/O on tick. Dig of virgin terrain at SoftCap may refuse — same honesty class as floor SoftCap.

**Deferred:** Blocks→Context; BoundedStore / VisibilitySystem; overlay eviction with I/O on tick / column rewrite.

**Adendo (jul 2026 — column index):** Secondary `_overlaysByChunk` map updated only via `StoreOverlay` / `RemoveOverlay` (same path as LevelDB hydrate/delete). `GetOverlaysInColumn` is O(bucket) instead of scanning all overlays. Flat `_blockOverrides` remains SSOT for `GetBlock` / `OverrideCount`. Compact-to-base removes keys; SoftCap refuses new keys (§36 SoftCap shipped adendo). Not zero-alloc — result list is still O(column size).

**Adendo (jul 2026 — overlay SoftCap deferred):** Audit pushed SoftCap-on-overlays like FloorDrop. **Not shipped:** Survival break of base terrain inserts a new air overlay key — SoftCap refuse would silently block dig after ~10k edits. Overlays stay **warn-only** until an eviction/compaction ADR (possibly drop air-overlays that match base). Floor SoftCap `2048` unchanged.

**Adendo (jul 2026 — overlay SoftCap shipped):** Prior deferral was too kind to grief. **Shipped:**

1. `World.OverrideSoftCap` (`10_000`): refuse **new** overlay keys; overwrite existing always OK.
2. **Compact-to-base:** writing rid == flat `SampleBaseBlock` removes the overlay key + `DeleteOverlayAsync` (frees SoftCap budget; place→break on sky cells no longer accumulate).
3. Dig of virgin base that needs a new air key **can** SoftCap-refuse after cap — honest LAN bound (resync, no orphan dumps). BlockSystem checks `CanAcceptBlockWrite` / `TrySetBlock` before chest dump / place consume.
4. **ChestStore.SoftCap** (`10_000`): `TryEnsure` refuses new cells; hydrate bypasses SoftCap.
5. Floor SoftCap `2048` unchanged. Full eviction / column rewrite still Deferred.

**Process (audit refresh):** dirty tree ahead of `origin` after closing a hygiene gap is a **process failure**, not WIP — commit+push same day. Leaf CI on push/PR is required; Bedrock E2E CI remains beta-hard (see [`robustness-dx-debt.md`](robustness-dx-debt.md)).

### 37. UpdateAbilities + AdventureSettings (spawn seed + fly echo)

**Choice:** After §34 `SetActorData`/`UpdateAttributes`, send `UpdateAbilities` (`0xBB`) then `UpdateAdventureSettings` (`0xBC`) before PreSpawn. Shared `AbilityData` writer (SSOT) also used by `AddPlayer` via wire `GameMode` int. Survival mask = pre-refactor golden bits; Creative = Survival + `MayFly` + `InstantBuild` + `Flying` (join already flying — intentional). `Invulnerable` off. Runtime id = unique id. Inbound `RequestAbility` (`0xB8`) for `FLYING` only: Creative echoes `SendLocalAbilities` with packet bool (stateless); Survival ignore (no kick). Adventure LAN defaults: ShowNameTags + AutoJump.

**Why:** StartGame gamemode without abilities is cosmetic; clients need ability layers for fly/instant-build feel. RequestAbility must be consumed or Warning-spams. Seed pattern matches §34 — no `Player.MayFly` / AbilitySystem.

**Deferred / next honesty:** hunger/food/fall damage. (`/gamemode` → §52; Death/Respawn → §40 adendo; drop-entity → §26.)

### 38. CraftCreative from CreativeCatalog SSOT

**Choice (updated Jul 2026):** `CreativeCatalog` on `ServerContext` (net ids 1–7). Protocol `BuildCreativeContent` from catalog. ISR `CraftCreative` is a first-class `InventoryStackAction` (same contract as `CraftRecipe`): materialize `MaxStack` into `CraftResultFlat` (CreatedOutput), then honor same-request Place/Take/Drop/Create. `NumberOfCrafts` on CraftCreative is protocol boilerplate (ignored; do not reject times==0). Recipe+Creative in one request rejected. Creative-only (handler + system). Wire `WireTouch` echoes; response includes CreatedOutput (60) for craft path.

**Why:** Palette vanilla is CreatedOutput→cursor/bag, not bag `TryAdd`. The prior `CraftCreativeNetId` parallel intent with empty Actions discarded client Places (click never reached cursor).

**Deferred:** full `creative_items.json` / `block_state_b64`; ActionDestroy creative trash (Unsupported → whole-request reject today).

### 39. Inventory + chest LevelDB persist (`ct:` / `inv:`)

**Choice:** Same world LevelDB as `c:`/`ov:`. Keys `ct:x:y:z` and `inv:{uuid:D}` with packed slot blobs. Puts remain fire-and-forget on the tick/network path; LevelDB tracks in-flight chest/inv tasks and `FlushAsync` awaits them on graceful shutdown (§41). World hydrates chests at boot; login `TryLoadInventory` before first content; quit enqueues Put. InMemory storage keeps ct/inv dicts for tests. No `players/` volume (§20).

**Adendo (jul 2026 — SlotBlob v2 / §55):** On-disk format is **`SlotBlob` version=2** (`u8 kind` + `i32 value` + `i32 count` per slot). Version=1 (`i32 value` + `i32 count`, value assumed Block) still **loads via migrate-on-read**: `Tools.IsTool(value)` → `StackKind.Item`, else `StackKind.Block`. New Puts always write v2. Ops: existing LAN worlds with v1 `inv:`/`ct:` upgrade transparently on read; no offline rewrite tool required.

**Why:** Process restart was wiping bags/chests — ops honesty without Mojang playerdata.

**Deferred:** Ender chest, armor. **Position + GameMode → §60**. **Double-chest → §56** (persist still 2×`ct:`). **`players/` volume** still Deferred (§20 / §60).

### 40. Vitals fields + void soft-rescue

**Choice:** `Player.Health`/`Hunger` (20/20) drive `UpdateAttributes` at spawn. Void: if `Y < FlatMinY - 8`, soft-rescue to **world spawn** `(0, FlatSpawnY, 0)` — **no** Health=0 / Respawn handshake (softlock risk). Same-XZ Y-only rescue left players inside dig shafts. Check lives in `MovementSystem` (Rule 7 — no decorative VitalsSystem). Self camera correction uses `MovePlayer` Teleport (§41); `MoveActorAbsolute` alone is insufficient for the local client under client-authoritative movement. Peers still get Absolute via dirty pose fan-out.

**Why:** Attributes literals lied about domain; falling forever was worse than thin vitals. Death wire deferred until respawn protocol is intentional.

**Deferred:** hunger tick, food, fall damage, drowning, death/Respawn packets.

**Adendo (jul 2026 — death / Respawn MVP):** Soft-rescue **superseded** once Respawn wire shipped. Void `Y < FlatMinY - 8` now sets `Health = 0`, `IsDead`, sends `DeathInfo` (`0xbd`) + `Respawn` SEARCHING (`0x2d`); client `Respawn` CLIENT_READY or `PlayerAction` RESPAWN → `SubmitRespawn` → GameLoop restores Health=20, pose to world spawn `(0, FlatSpawnY, 0)`, Teleport + attributes + Respawn READY, then **InventoryContent + UiInventoryContent** (client clears bag UI on death). **Death loot → §73** (Survival dumps to floor; Creative keepInventory). AuthInput / block edits ignored while dead. No VitalsSystem / DamageSystem; logic stays in `MovementSystem` + handler intents. Hunger/food/fall/drowning still Deferred.

### 41. Void MovePlayer Teleport + shutdown persistence flush

**Choice:** Soft-rescue self → `EntityProtocol.SendMovePlayerTeleport` (`MovePlayer` mode Teleport, cause Command). Graceful stop: `IChunkStorage.FlushAsync` (InMemory noop; LevelDB `WhenAll` pending Puts + `DrainOverlayWrites`) via `World.FlushPersistenceAsync`, then dispose storage, then RakNet. `Program` completes the same shutdown path from **CancelKeyPress**, **SIGINT**, and **SIGTERM** (Docker/Dokploy stop) — 5s flush timeout → Warning. Flush **never** on GameLoop tick (ADR §13).

**Adendo (jul 2026 — shutdown DisconnectPacket):** Before LevelDB flush / UDP close, `ZenithSessionListener.DisconnectAll("Server closed")` sends Bedrock `DisconnectPacket` (Immediate) + `FlushOutgoing` then RakNet close (no fixed sleep — drain outbound frames synchronously). Abrupt socket death left clients on “host lost” / stuck LAN error UI. MOTD pong trailing `0` matches common Bedrock list clients; RakNet GUID persisted in `server.guid` so LAN identity is stable across restarts.

**Why:** Own camera stayed in void after Absolute-only rescue; Ctrl+C could drop last bag/chest Puts; container SIGTERM previously skipped flush entirely. Operational trust before death/drop-entity surface.

**Smoke:** S39 graceful restart / stop; S40 own-camera snap to world spawn (ARCHITECTURE.md). Ctrl+C with players online → clean disconnect screen.

### 42. Dig crack LevelEvent peer fan-out

**Choice:** Keep `WorldProtocol.SendBlockStartCrack` / `SendBlockStopCrack` session-scoped (Protocol transmits only). Callers use `BlockCrackFanout.Start/Stop(PlayerManager, minerSession, …)` — miner + every other `IsInGame` peer (same Online loop as EquipmentSystem / UpdateBlock). No VisibilitySystem. Dig rarity ≠ §24 hot path.

**Why:** Self-only crack made MP dig look single-player; peers never saw progress. Fan-out is the smallest honest fix without radius culling abstractions.

**Smoke:** S41 (ARCHITECTURE.md). Gate: fail S41 → do not open death/drop-entity.

### 43. Protocol mismatch UX (+ EventBus negotiate seam)

**Choice:** After `NetworkSettings`, `ProtocolGate.Evaluate(client, ServerIdentity.ProtocolVersion)` decides Accepted / FailClient / FailServer. Reject → `LoginProtocol.SendIncompatibleProtocol` (`PlayStatus` 1 or 2) + disconnect; no `Player`. Re-check on `LoginPacket.Protocol`. Before accept/reject, publish mutable `ProtocolNegotiateEvent` (Accepted / RejectPlayStatus) so a future listener can override the gate — **Accepted ≠ second codec** (document: forcing accept without encode support breaks on wire). Multi-codec / YAML supported-protocols / plugins: **Deferred**.

**Adendo (jul 2026 — PlayStatus flush):** Incompatible `PlayStatus` is sent **Immediate** + `NOT_PRESENT` compression, then `FlushOutgoing` before RakNet close. Prior Normal-priority PlayStatus was often lost when `Disconnect` Immediate flushed one random frame and closed — vanilla outdated UI never appeared (PlayStatus alone is enough; no custom Disconnect string required).

**Why:** Wrong-version clients previously hung or logged in without Bedrock’s classic incompatible UI. Gate stays pure; Protocol only transmits; Handler orchestrates. Cheap multiprotocol seam without inventing PluginAPI now (Rule 7).

**Smoke:** Connect with client protocol ≠ server → Bedrock outdated client/server UI + disconnect.

### 44. Tick peer egress (dirty pose + Online snapshot + GamePacket batch)

**Choice:** `MovementSystem` applies AuthInput then fans Absolute only when pose floats (XYZ+Pitch+Yaw+HeadYaw) **or on-ground** differ from `LastReplicated*` (exact equality; look-only must still fan — same AuthInput channel). `PlayerManager.Online` is a single allocating snapshot (`IReadOnlyList`); Tick / nested fan-out capture once (`var online = _players.Online`) and reuse. Dirty movers aggregated per peer via `EntityProtocol.SendMoveAbsolutes`; tick `UpdateBlock`s via `WorldProtocol.PublishUpdateBlocks` — both reuse `SendDataPacket(params)` (one envelope / peer). No VisibilitySystem.

**Why:** Idle AuthInput was O(n²) Absolute spam; repeated Online snapshots nested in loops allocated per mover; one Absolute/UpdateBlock per call wasted RakNet frames. Batch is frame reduction, not a §24 zero-alloc claim.

**Smoke:** Idle LAN — peers idle without Absolute flood; look-turn still updates peer yaw; multi-place same tick → one envelope per peer.

**Adendo (jul 2026 — join settle):** Dirty-check left late joiners with AddPlayer only until the subject moved — peer entities often floated until Absolute. `PlayerVisibility.SendAddPlayer` now follows with `MoveActorAbsolute` (`FLAG_ON_GROUND`, feet + network offset) for that recipient so new viewers settle without forcing idle Absolute spam.

**Adendo (jul 2026 — Absolute ON_GROUND):** Continuous Absolute used `flags=0`. Bedrock then applies local gravity to remote players; after they stop moving, peers look “frozen”/sunk while the stationary client still sees others fine. AuthInput **VerticalCollision** (bit 50) → `Player.IsOnGround` → `MoveActorAbsolute.FLAG_ON_GROUND`. On-ground changes dirty Absolute even when XYZ/look unchanged.

### 45. Sparse flat columns (miss without Put)

**Choice:** `GetOrCreateColumnAsync` on storage miss returns shared in-memory flat payload **without** `PutAsync` under `c:x:z`. Existing terrain blobs reused. Legacy empty/corrupt (`!LooksLikeTerrainPayload`) still regenerates flat + Put (disk self-heal). Overlays remain `ov:` only.

**Why:** Exploring virgin chunks previously filled the MemTable with identical flat blobs. Flat authority is procedural + overlay — `c:` was already optional.

**Smoke:** Explore far without LevelDB growth proportional to columns visited; overlays still persist across restart.

### 46. Chest facing only (no double-chest)

**Choice:** Palette secondary index `name + one string-state → network_id` (chest `minecraft:cardinal_direction`). Live place remaps chest item rid → facing from player yaw in `InGameSessionHandler` (front toward player = opposite look). `Blocks.Chest` remains south (item/recipe/creative). `IsChest` covers all four rids; break drops canonical `Blocks.Chest`. `BlockSystem` does not reorient intents submitted as raw south (tests / tools).

**Why:** Adjacent south-south chests client-merge as a visual double while server keeps 2×27 (`ct:`). Correct facing is medium polish without BlockActor / pair model.

**Deferred:** Trapped/ender/copper; stairs/beds/doors facing stack. **Double-chest → §56.**

**Known (historical):** Two adjacent chests with the same cardinal still meshed as a double on the Bedrock client while server kept 2×27 — fixed by §56 pair open.

**Smoke:** Place chest while looking each cardinal → UpdateBlock rid matches; break always returns stackable south item; open/break works on any facing.

**Adendo (jul 2026 — PlaceFacing):** Yaw→cardinal (+ opposite / front-toward-player) lives in `PlaceFacing`; `ChestFacing` is a thin chest place wrapper. Next directional block reuses the helper and stays allowlisted in `Blocks` — still no furnace/stairs product stack, BlockBehavior framework, or double-chest (roadmap / Deferred above). Lid wire is §28 adendo.

### 47. MOTD online count + ghost session hygiene

**Choice:**

1. `RakNetServer.OnlinePlayerCount` (`Func<int>?`) wired to `PlayerManager.Count` — MOTD online is players, not `ConnectionCount` (avoids `Connections.ToList()` on ping).
2. Bedrock `DisconnectPacket` → `session.Disconnect()` in PreSpawn + InGame (closes transport + `HandleClose`).
3. At `MaxConnectionsPerAddress`, OCR2 **evicts** one same-IP session (`HasGameIdentity == false` preferred, else oldest `LastSeen`) then accepts — does not only reject.
4. Login displace: if `TryAdd` fails, disconnect existing same username and retry.

**Why:** Quit without RakNet teardown left UDP sessions until 15s timeout; MOTD inflated; rapid rejoins from new ephemeral ports burned `max-players-per-ip` (default 3) while the host still saw one in-world player.

**Deferred:** Shorter global idle timeout; kicking live NAT roommates beyond unbound-prefer (RakNet digest mute → §50).

**Smoke:** Peer leaves → MOTD shows 1 (host). Rejoin repeatedly from same IP without wedging at 3.

### 48. Folder layout = architecture roles (no Network/ junk drawer)

**Choice:** Promote `Packets/`, `Protocol/`, and `Session/` to siblings under `src/zenith/` (namespaces `Zenith.Packets` / `Zenith.Protocol` / `Zenith.Session`). Relocate glue: `ProtocolGate` + `ColumnSend` → Protocol; `ZenithSessionListener` + `PlayerVisibility` → Session. Delete `Network/`. Fix Packets→domain leaks (`StartGame` version strings from Protocol; `ItemRegistryWireEntry` DTO; drop unused World using on CreativeContent). Refresh ARCHITECTURE Layout + dx workflow.

**Why:** ~⅔ of the server tree lived under `Network/`, mixing serialize / transmit / session SM — folders contradicted ARCHITECTURE roles (decide ≠ transmit ≠ serialize).

**Deferred:** Split `World/` (ItemPalette / chests → `Item/` etc.); rename type `NetworkSession`.

### 49. PlayerSkinPacket wire + Session relay (no Player→Packets)

**Choice:** `SerializedSkin` DTOs stay in Packets (`ref BinaryStream` Write/Read, `PieceType` string, list/image caps). Wire skin state lives on `NetworkSession.Skin` (not `Player` — no Player→Packets). `Player` keeps only `SkinRgba`/`SkinWidth`/`SkinHeight` as classic mirror / PlayerList fallback. Inbound `PlayerSkinPacket` validates UUID == player, updates Session skin + RGBA when classic image is well-formed, relays via `PlayerVisibility.RelaySkin` to other InGame peers (Session helper — same join-like orchestration as `AnnounceJoin`; not a SkinSystem/ECS). `IsVerified` relayed as decoded.

**Why:** Collaborator merge landed broken encode (`BinaryStream` by value), `Player`→Packets leak, self-only echo, and `PieceType` as uint. Chat already forbids handler fan-out loops; skin is rare one-shot so Session helper is enough.

**Smoke:** A changes skin in-game → B sees update. Spoofed UUID ignored.

**Adendo (jul 2026 — Encode Id):** Outbound `PlayerSkinPacket.Encode` must prefix packet Id (`0x5d`) like every other clientbound DataPacket. Missing Id made peer relay silently ignored by Bedrock (inbound Decode unchanged — header already stripped).

**Adendo (jul 2026 — Join skin = product path):** Mid-game change is optional; peers must see the join skin without anyone re-equipping. Parse ClientData JWT via `ClientSkinParser` (Dragonfly `parseSkin` / PocketMine `ClientDataToSkinDataHelper`: SkinData + geometry + cape + animations + persona pieces/tints + flags). `PlayerList` ADD writes full `SerializedSkin.Write`; SkinWire RGBA/placeholder only if parse fails. Endstone/BDS builds `SerializedSkinRef` from `ConnectionRequest` the same way. `TrustedSkin` → PlayerList verified flag.

**Adendo (jul 2026 — ClientProfile):** XUID / device / platform chat id on join → §59 (same Session-owned pattern as Skin).

### 50. Split log levels (server vs RakNet)

**Choice:** Two `Logger` instances at composition root (`ZenithServer`), levels from `zenith.yml` `log.server` / `log.raknet`. Aliases (`none`|`info`|`warn`|`error`|`debug`|`all`) map to **severity ladders** on the existing flag enum (`info` = Info|Warning|Error). Defaults ops-first: server `info`, raknet `warn`. YAML without a `log:` block keeps C# defaults (no rewrite of operator files). No Serilog, no categories, no call-site changes in `raknet`.

**Why:** Shared `LogLevel.All` made Bedrock Debug unusable — datagram spam drowned join/place. Transport is mature; gameplay is the active debug surface. Rule 7: two loggers beat a logging framework.

**Smoke:** Default boot — join Info, no `Connected PID` flood. `log.raknet: debug` + `log.server: info` → only transport Debug.

### 51. Collaborator UI/sound/emote packets — wire hygiene (not product feature)

**Choice:** Keep Toast / PlaySound / StopSound / ModalForm / ServerSettings / CloseForm / Emote / EmoteList as Packets DTOs. Fix Encode (packet Id prefix), Bedrock wire shapes (SoundPos×8, Emote unsigned runtime + byte flags, EmoteList count/runtime), and expose transmit-only `UiProtocol` + `WorldProtocol.SendPlaySound/StopSound`. Inbound Emote / EmoteList / ModalFormResponse / ServerSettingsRequest stay on the quiet ignore-list (no Systems stub). Round-trips in `zenith.Tests`.

**Why:** Contributor landed DTOs without Id-in-Encode and with broken EmoteList handler decode — unusable outbound and Warning-prone inbound. Wire fix ≠ shipping toast/forms/emote product; H1 smoke stays death/drops.

**Deferred:** Form intent stack; FormId allocator + mid-session refresh (re-send `ModalFormRequest` / ServerSettings with the same id — **not** `NetworkSettings`, which is login compression); product title banners (TextObject / timed UI as gameplay). (Emote peer relay → §53.)

**Adendo (jul 2026 — SetTitle wire):** `SetTitlePacket` (0x58) + `UiProtocol.SendTitle` / `SendSubtitle` / `SendActionbar` / `SendTitleTimes` / Clear / Reset shipped as transmit hygiene. Times is a separate packet from text. Inbound ModalFormResponse remains quiet ignore (no Info decode path). Product title use stays Deferred.

### 52. `/gamemode` mínimo (sem command framework)

**Choice:**

1. Inbound `/gamemode` via **`CommandRequest` (0x4D)** (canal Bedrock) ou `Text` chat (fallback). Args: `survival|creative|s|c|0|1`. Outros `/…` quiet ignore (no registry / `/help` / autocomplete).
2. Valid parse → `Player.SubmitGameMode` (overwrite-latest intent). **Never** `SubmitChat` for slash / command lines — `ChatSystem` must not fan-out commands to peers.
3. `GameModeSystem` on tick: `SetGameMode` (private set; inventory **not** reseeded) → Protocol `SetPlayerGameType` + `SendLocalAbilities` + `SendAdventureSettings`; remint `CreativeContent` on **every** mode change (PocketMine `syncGameMode` → `syncCreative`); Toast feedback via existing `UiProtocol`.
4. Bad `/gamemode` args → same-session Toast (handler decide + transmit; no world mutation). No Gameplay→Packets.

**Why:** H1-3 LAN need — Survival↔Creative in-session without restart/`zenith.yml`. Modes/abilities already exist (§31/§37). Rule 7: one string parse beats a command framework (`dx.md` freeze). Exception to explicit non-goal “`/` commands” — **one** path only, documented here like void-death in MovementSystem.

**Deferred:** Adventure/Spectator; permissions; Bedrock `AvailableCommands` / autocomplete; `/` registry.

**Smoke:** A in Survival `/gamemode creative` → fly/instant dig + Creative UI; `/gamemode survival` → back; peers do not see the slash text in chat; held/dig follow new mode. No `Unhandled Data Packet: 77`.

**Adendo (jul 2026 — CommandRequest canal):** Bedrock 1.26 envia slash como `CommandRequest` (0x4D), não `Text`. Handler parseia `CommandLine` com o mesmo `GameModeConfig.TryParseCommand` (Text `/gamemode` fica fallback). Origin wire (protocol 1001): origin **string** (`player`, …) + UUID + requestId + **Int64** player unique id (sempre). Sem `AvailableCommands` / `CommandOutput` / palette — quiet ignore outros comandos. Não mergear `dev/commands` framework.

**Adendo (jul 2026 — peer re-AddPlayer):** Closed by §59 — `GameModeSystem` calls `PlayerVisibility.RefreshPeerView` (RemoveActor + AddPlayer + Absolute settle) so peers see the new mode without rejoining. PlayerList is not removed/re-added.

**Adendo (jul 2026 — CreativeContent remint matrix):** Join always sends `CreativeContent` once after `ItemRegistry` (PM PreSpawn `syncCreative`, Dragonfly session start, Serenity spawn) — **including Survival**; the packet seeds the catalog, gamemode/abilities gate the UI. Seeing CreativeContent on Survival join + again on `/gamemode creative` is **expected** (join + remint), not a double-send bug in one stage. Cross-ref: PM remints on every `syncGameMode`; DF/Serenity remint only at join and rely on abilities for mid-session switches. Zenith follows **PM** for remint-on-change.

### 53. Pose flags + emote relay + arm swing (peer interaction feel)

**Choice:**

1. **Sneak / sprint (Leaf A):** AuthInput bitset → `MovementInputState` (handler queues only) → `MovementSystem` applies `Player.IsSneaking` / `IsSprinting` → dirty `SetActorData` **FLAGS-only** to other InGame peers via `EntityProtocol.SendActorFlags`. No PoseSystem / ECS / VisibilitySystem. Protocol 1001 bit indices (Endstone): continuous `Sneaking=8`; sprint edges `StartSprinting=25` / `StopSprinting=26`. Actor FLAGS: `SNEAKING=1`, `SPRINTING=3`. Mutual exclusion (sprint clears sneak and vice versa) in `MovementSystem`. Spawn / AddPlayer / local seed use full metadata with pose bits OR’d in. Height stays **1.8** this leaf.
2. **Emote (Leaf B):** Inbound `EmotePacket` → validate runtime id == self → rate-limit (1s) → `PlayerVisibility.RelayEmote` (Session helper, like skin) with `FlagServerSide | FlagMuteChat`. EmoteList stays quiet ignore. No EmoteSystem.
3. **Arm swing (Leaf C):** AuthInput `MissedSwing=39` → tick fan-out; dig start / place queue / creative break / `ItemUseOnActor` Attack / UseClickAir → `PlayerVisibility.RelaySwingArm` (immediate). Wire (protocol 1001): action **u8**, runtime id, **data f32** (0), optional swingSource string (`attack` / `mine` / `build`). Inbound client `Animate` stays quiet ignore (no rebroadcast). No combat damage.

**Why:** Peers only saw XYZ+look; crouch/sprint/emote/swing are the minimum LAN “other player is alive” signals. Same decide≠transmit≠serialize path as Absolute (§44) and equipment (§18).

**Deferred:** Swim/glide/crawl flags; sneak BB height (~1.5) + WIDTH/HEIGHT metadata; ActorEvent arm-swing; client Animate relay. (sneak-place-on-chest → §56; LevelSound peer → §59.)

**Smoke:** A holds sneak → B sees crouch; A sprints → B sees sprint; late join while A sneaks → AddPlayer metadata crouch; A emotes → B plays emote; A swings at air → B sees arm swing (Animate 1001: u8 action + runtime + f32 data + optional swingSource string).

**Adendo (jul 2026 — Mojang protocol docs):** Official docs clone at `~/Development/references/bedrock/bedrock-protocol-docs` branch `r/26_u4`. That tip stamps protocol **2169** / game **1.26.50**; Zenith speaks **1001** / **1.26.33**. Use docs for packet field trees; use PM BedrockProtocol 1001 + Endstone headers for AuthInput bit indices and 1001-era Animate swingSource (optional string). Quiet-ACK `SetPlayerInventoryOptions` (`0x133` / 307) — UI prefs only, no product leaf.

**Adendo (jul 2026 — LevelSound peer):** Closed by §59 — server-authored `LevelSoundEvent` (`place` / `break` / `hit`) from `BlockSystem` via `BlockSoundFanout`. Inbound client LevelSound stays quiet ACK (no echo).

### OpenInventory / chest UI (note under §28)

**Superseded for mutation by §54 Phase 2:** handler queues open/close intent; `OpenChest` / `InventoryWindowOpen` apply on GameLoop tick; same-session `ContainerOpen` / contents still Protocol after decide. Slot mutations stay ISR → `InventoryStackIntent` → `InventorySystem`.

### 54. Platform health (Robustness / DX) — separate from Horizon‑1 product

**Choice:**

1. Platform-health leaves (Online-once, dig/UI on tick, zero-alloc hot pack, DX cleanup, InventoryProtocol split) are tracked in [`robustness-dx-debt.md`](robustness-dx-debt.md), **not** as Horizon‑1 product rows.
2. Three baskets: **A** = roadmap product (tools, double-chest, …); **B** = cross-thread / fan-out / GC / fat handler; **C** = docs/dead code. Phases address B then C; A stays in [`roadmap.md`](roadmap.md).
3. Freeze list unchanged: no Scheduler, Actor/ECS, VisibilitySystem, DI, plugin API, `/` command framework, `Network/` revival.
4. C# modernization only on measured hot paths (`Span` / `ArrayPool` / reused lists) — no mass primary-ctor / switch rewrite.
5. One leaf ≈ one ADR adendo (or §54 sub-leaf) + one PR.

**Why:** Spine is healthy; rediscovering “why not ECS” every PR wastes context. Debt doc is the cite target.

**Adendo — Peer FX rule (Phase 2):** Peer-visible FX (crack, swing, Absolute FLAGS, equipment, chat) apply on GameLoop tick. Documented exceptions: Session helpers for join/skin/emote (already §49/§53). Crack + dig swing move to tick with dig intents. §53 “immediate” dig/place swing is updated: dig start/abort swing fans on tick; place/attack/missed-swing paths keep tick or Session helper as already specified.

**Adendo — SelectedHotbarSlot:** Handler may write `SelectedHotbarSlot` as overwrite-latest input (like movement). EquipmentSystem fingerprints held on tick — no locks on Player fields.

**Adendo — InventoryProtocol split (Phase 5):** `InventoryContainerMap` (wire map SSOT), `CreativeContentBuilder`, `CraftingDataBuilder`, `InventoryNetIds` are Protocol-adjacent files; `InventoryProtocol` remains the transmit façade. No new layer names / Mapper framework. `World/` → `Item/` folder cut stays deferred (debt doc).

**Adendo — stack net id wire contract:** Callers use `DescribeForWire(flat, slot)` (refresh + DTO) — never ad-hoc Refresh+Get. `MatchesAdvertisedStackNetId(flat, clientId)` soft-checks ISR: `clientId ≤ 0` accept; positive must equal last advertisement; mismatch → Error + full inventory/UI/(chest) resync before mutate. Domain `InventorySlot` still has no id (DF-style deferred). Negative prediction ids deferred (§35).

**Adendo (jul 2026 — Online-once closed):** GameLoop `FillOnline` once/tick; systems take `online` and must not call `PlayerManager.Online` on the tick path (`ApplyWindow`, void-death, etc.). Test overloads use a per-system `_onlineScratch` + `FillOnline`. Session helpers use `SnapshotOnline()`. `GamePacket.EncodeOwned` already avoids Encode+ToArray double copy. Gameplay must not `using Zenith.Packets` — Respawn states via `EntityProtocol.SendRespawnSearching` / `SendRespawnReady`.

**Deferred:** `World/` → `Item/` folder cut until H1 registry/dimension forces it (see debt doc). Domain-owned stack net ids / NBT identity.

### 55. Block/item foundation — StackId + sparse capability profiles

**Choice:**

1. **Three layers (always):**
   - **A — Wire registry:** full `BlockPalette` / `ItemPalette` dumps; any dump rid may exist in overlay (passthrough).
   - **B — Stack identity:** `StackId(StackKind, Value)` — `Block` = block runtime id; `Item` = item network id. Inventory / chest / craft / floor drops use `StackId`, not a bare overloaded `int`.
   - **C — Sparse capability profiles:** per-axis maps in `World/` (`DigProfiles`; tool data via public façade **`Tools`** = the ToolProfiles map — one public name) — composition without OOP `Item`/`Block` hierarchies.

2. **Mojang glossary (SSOT names in new code):**
   - `BlockRuntimeId` — block palette network/runtime id  
   - `ItemNetworkId` — item palette id (i16 on wire)  
   - `StackNetworkId` — ISR session prediction id (unchanged)  
   - `EntityRuntimeId` — actor/entity long id  
   - Dig hardness field = **`DestroySpeed`** (= Endstone dump `destroy_speed` / wiki hardness)  
   - Domain chest stores ≠ BDS `BlockActor` until a dedicated ADR  

3. **Unknown Survival dig:** block rid **without** `DigProfiles` entry → do **not** authenticate dig (no start / reject predict + resync). **Not** silent `DestroySpeed=2`. Creative InstantBuild unchanged. Missing profile ≠ future “unbreakable” profile (`Breakable=false` explicit later).

4. **Place vs dig:** `Blocks.IsPlaceable` allowlist independent of dig profiles.

5. **Persistence:** `SlotBlob` v2 stores kind+value; v1 migrate-on-read (`Tools.IsTool(id)` → Item, else Block). Floor cells store `StackId`.

6. **Cross-layer rule:** Overlay/`GetBlock` = `BlockRuntimeId` only. Inventory/chest/craft/floor = `StackId`. Dig = `(BlockRuntimeId, held StackId)`. Protocol is the primary place that maps `StackId` → `NetworkItemStack`. `Blocks.NormalizeMerge*` / facing normalize = `StackKind.Block` only. Never compare `.Value` across kinds.

7. **Reserved:** optional `StackUserData` (item NBT/damage) later — not in `StackId.Value`.

8. **Non-goals:** PM/DF virtual `Item`/`Block` trees; BDS BlockActor/component graph; redstone `CircuitSystem`; plugin block API; top-level `Capabilities/` folder until several axes force it (profiles stay under `World/`).

**Why:** Overloaded `InventorySlot.RuntimeId` (block **or** tool) and silent dig defaults would force a larger inventory/persist rewrite later. Sparse profiles match Dragonfly/Endstone *capability axes* without freezing Rule 7. Wire dumps stay complete; Survival honesty stays curated.

**Contributor recipe:** see [`docs/dx.md`](dx.md) “Adding block/item capabilities”.

**Spike (same ADR era):** introduce `StackId`, migrate stores + Protocol, wrap dig behind `DigProfiles`, unknown dig policy — product dig formula unchanged (§27).

### 56. Double-chest — pair model + 54 UI (no BlockActor)

**Choice:**

1. **Pair resolve (World):** two axis-adjacent chest cells, same Y, **same** `minecraft:cardinal_direction`. Pair axis = perpendicular to facing (N/S face → ±X neighbors; E/W face → ±Z). No BlockActor / tile-entity graph.
2. **Primary (UI left half):** lexicographically smaller `(X, Z)` of the two cells (Y equal). Wire slots `0..26` = primary cell; `27..53` = partner.
3. **Storage:** still **two** `InventorySlot[27]` keyed by cell (`StackId`). Persist remains **`ct:x:y:z`** SlotBlob v2 per cell (§39) — never a single 54-blob.
4. **Open view:** `Player.OpenChest` is `OpenChestView?` (primary + optional partner + `SlotCount` 27|54). Empty-hand (or non-sneak interact) on either half opens the **pair** UI when a partner resolves. Same-packet AuthInput sneak is read via `TryPeekMovementInput` (not only last-tick `IsSneaking`).
5. **Sneak-place:** holding a placeable + sneaking + click chest → place on clicked face (do **not** open). Non-sneak + click chest → open (even with a held item). Chest place next to a compatible neighbor forms a pair for subsequent opens (adjacency resolve — no separate pair registry).
6. **Lid:** opener refcount on **both** cells when double; BlockEvent fan-out both positions (§28).
7. **Break:** dump/dissolve clicked cell only; partner stays single 27; clear openers on both if either was open; peers with that UI open get closed.

**Why:** §46 admitted client-mesh double while server kept 2×27 — honesty gap for LAN. Pair-at-open + sneak-place matches Bedrock UX without BlockActor.

**Non-goals:** trapped/ender/copper; hopper; left/right palette `type` states unless smoke forces them; 54-slot LevelDB blob.

**Domain flats:** chest open slots remain `ChestBase` (100) + 0..53; craft UI moved to `CraftUiBase` **200** so 54-slot chest flats do not collide with craft (was 150).

**Supersedes:** §28/§39/§46 Deferred “double-chest” lines — product leaf is this ADR.

### 57. Block gravity — sand/gravel cell tick (no Tile / FallingBlock framework)

**Choice:**

1. **Domain:** curated gravity set only — `minecraft:sand` + `minecraft:gravel` (add gravel to `Blocks` placeable / DigProfiles / Creative short list with sand). No concrete powder, anvil, dragon egg, scaffolding, or snow in this leaf.
2. **Support rule:** a gravity block is unsupported when the cell immediately below (`y-1`) is air **or** outside world vertical bounds (same FlatMinY floor as place). Any non-air below = supported (chests, overlays, flat base stone/dirt/grass all count — no full collision/shape table).
3. **Tick model (no Tile):** `GravitySystem` on GameLoop, after `BlockSystem` in registration order. World holds a sparse **pending fall** set of cell keys (reuse FloorDropStore SoftCap pattern — warn + refuse new keys past cap; merges/refreshes of existing keys OK). **Not** a BlockActor graph, **not** `Capabilities/`, **not** WorldEntity/ECS.
4. **Triggers (decide on tick, never on RakNet thread):**
   - After a successful place/break mutation that may unsupport neighbors: enqueue the placed cell if gravity; enqueue the cell **above** a broken/replaced cell if that above cell is gravity.
   - Optional same-tick flood: while draining, if a fall lands and the new above cell is gravity, enqueue it (column cascade) — bounded per tick (e.g. max N cell steps globally) so one dig under a sand tower cannot stall the loop.
5. **Fall step:** for each pending cell, if still gravity + unsupported: `SetBlock` source → air, `SetBlock` landing → rid (scan down to first supported cell + 1). Fan-out both cells via existing `UpdateBlock` peer path (`BlockSystem` helpers / `WorldProtocol.PublishUpdateBlocks`). **No** `AddActor` falling_block entity wire in MVP — client sees discrete cell moves; LAN honesty > vanilla fall animation.
6. **Landing / crush:** land on first non-air below; if scan hits FlatMinY with only air, destroy the falling block (no void entity). **No** player/entity crush damage; **No** item drop on “break mid-air” (block either lands or vanishes at void).
7. **Persistence:** falls are RAM pending only. On graceful stop, either drain all pending to settled overlays before flush **or** document “in-flight falls lost on hard kill” (same class as unflushed Puts). Settled cells remain normal `ov:` overlays — no gravity-specific LevelDB keys.
8. **Cross-layer:** Gameplay decides + mutates World; Protocol only UpdateBlock. Packets stay free of World/Player. Do **not** put gravity flags on DigProfiles — separate sparse map or `Blocks.IsGravity(rid)` allowlist (§55 heavy domains stay off DigProfile columns).

**Why:** Static sand/gravel is a visible LAN honesty gap after place/break. Cell-tick + UpdateBlock reuses overlay mutation and avoids freezing Tile/FallingBlock/Actor frameworks (Rule 7 / §32).

**Non-goals:** falling_block AddActor animation; anvil/concrete powder; fluid interaction; redstone dust pop; player crushing; mid-air break into FloorDrop; Mojang random-tick; gravity under chests opening; soft-block “partial support” shapes.

**Spike order:** (1) `Blocks.IsGravity` + gravel placeable/dig/creative, (2) pending set + `GravitySystem` drain after BlockSystem, (3) place/break enqueue hooks, (4) leaf tests (tower, dig under, place over air, SoftCap), (5) human smoke.

**Status (jul 2026):** Shipped on develop after `v0.0.2-alpha` — `GravityPendingStore` + `GravitySystem`; gravel in placeables / DigProfiles / CreativeCatalog (net id 20); graceful shutdown settles pending before flush.

**Adendo (jul 2026 — fall rate):** Playtest: `MaxStepsPerTick = 64` collapsed whole towers in one tick (~instant). Dragonfly uses `falling_block` entities (gravity 0.04/tick). Zenith now **`MaxStepsPerTick = 2`** + **deferred enqueue** for cascade cells (processed next tick, not same-tick chain). Shutdown `SettleAllPending` drains immediately without defer.

**Roadmap:** H1#6. Playerdata reconnect → §60 (H1#7). Does **not** unlock world-gen (H1#8).

### 58. Protocol smoke bot — separate Bun repo (not inside Zenith C#)

**Choice:**

1. **Location:** dedicated repo [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) (sibling checkout). **Not** under `zenith/tools/` or `src/zenith/` — keeps the server tree C#-only (no Node/TS mixed into product PRs).
2. **Stack:** [Bun](https://bun.sh) + Prismarine [`bedrock-protocol`](https://github.com/PrismarineJS/bedrock-protocol). Offline/`self-signed` LAN auth for local + future CI. **Not** Mineflayer (Java). **Not** Bedrock Launcher mods as the CI path.
3. **Role:** automate **repetitive wire smokes** (join → InGame, later place/break/chest open). Complements human Gate A; does **not** replace official-client UI/mesh/crash checks.
4. **Boundary:** bot is a normal Bedrock client. No references into Zenith Gameplay/World; Zenith does not depend on the bot.
5. **CI:** Zenith leaf `dotnet test` stays the PR gate. Bot is **opt-in** until a job boots Zenith + runs join. Do not block Zenith merges on bot harness immaturity.
6. **Version pin:** `createClient({ version: "1.26.30" })` — minecraft-data protocol **1001**, matches Zenith `ServerIdentity.ProtocolVersion`. Zenith `VersionName` is `1.26.33` (same protocol id; document skew in bot README).

**Why:** In-monorepo `tools/smoke-bot/` mixed JS into a C# product and was rejected for DX/review clarity. A second repo is acceptable when the tool is a different language and must not pollute server PRs; pin docs + README keep protocol skew honest.

**Supersedes:** brief in-monorepo spike on `develop` (`9fa6f29`) — removed; use the dedicated repo.

**MVP:** `bun run smoke:join` — connect offline → spawn → exit 0/1. First-wave #2–#10 (`place` … `two-client` + `graceful-persist`) live in the same repo (`bun run smoke:first10`).

**Non-goals:** full Mineflayer API; Xbox auth in CI; launcher injection; replacing human Gate A for `v0.0.2-alpha`; embedding Node in the Zenith solution.

### 59. Join wire fidelity (ClientProfile + gamemode peers + LevelSound)

**Choice:** Close the remaining skin-class gaps where login discarded ClientData/identity and join packets lied:

1. **`ClientProfile` on `NetworkSession`** (not `Player`): XUID string, DeviceId, BuildPlatform (`DeviceOS`), PlatformChatId (`PlatformOnlineId`). Parsed at login from identity JWT/chain `xid`/`XUID` + ClientData. Feeds `PlayerList` ADD (XboxUserId / BuildPlatform), `AddPlayer` (DeviceId / BuildPlatform / PlatformChatId), `ChatSystem` TextPacket XUID, and emote relay XUID preference. Same Session-owned wire-state pattern as §49 skin.
2. **`/gamemode` peer honesty:** `GameModeSystem` after self wire → `PlayerVisibility.RefreshPeerView` (RemoveActor + AddPlayer with current GameMode/pose/held + Absolute settle). No PlayerList churn.
3. **LevelSound peer fan-out:** `LevelSoundEventPacket` (0x7b, PM 1001 string sound names) + `WorldProtocol.SendLevelSoundEvent` + `BlockSoundFanout` (all InGame peers, like crack — not Knows-column). `BlockSystem` emits `place` / `break` / `hit` with ExtraData = block runtime id. Inbound LevelSound remains quiet ACK — Dragonfly `ViewSound` model, not client echo.
4. Hygiene: `ResourcePacksInfo` world-template UUID = `Guid.Empty` via `WriteUuid` (no pack product).

**Why:** Peers must see join identity and hear/see block FX without mid-game workarounds. Rule 7: Session profile + existing visibility/fan-out helpers beat VisibilitySystem / sound frameworks.

**Smoke:** Xbox/LAN XUID on PlayerList + chat when present; A `/gamemode creative` → B sees mode without rejoin; A place/break → B hears sound; client LevelSound spam ignored.

**Non-goals:** armor/offhand, EmoteList product, Adventure/Spectator, AvailableCommands, client Animate rebroadcast, sneak BB height, resource-pack product, gravity/biomes (playerdata → §60).

### 60. Reconnect playerdata (`pd:{uuid}` pose + GameMode)

**Choice:** Persist feet XYZ + yaw/pitch + GameMode in the **same world LevelDB** as `inv:` / `ct:`, key `pd:{uuid:D}` lowercase. Blob v1: `u8 version` + 5×`f32` + `u8` mode (Survival=0 / Creative=1). Load on login after inventory, **before** StartGame; Put on quit and after `/gamemode` apply. Unstable identity skips Put. OOB / NaN / bad mode → load miss (flat spawn + config mode) — no silent clamp. Death/void Respawn still world spawn (§40) — only reconnect restores.

**Why:** Softcores (PMMP `players/*.dat`, Dragonfly/PNX `players/` LevelDB) diverge from Mojang BDS, which co-locates `player_` / `player_server_` NBT in the world DB. Zenith keys are already custom; another prefix does not worsen future Mojang import. A new `players/` volume would add ADR §20 path surface and lock into the softcore split. **Reserve** Mojang namespaces (`player_`, `player_server_`, `~local_player`) — never write them in this leaf.

**Flush:** same fire-and-forget + pending-task + shutdown `FlushAsync` as inventory (§41). No mid-tick pose Puts. Unstable identity skips **all** `inv:` / `pd:` Puts (quit + systems).

**Smoke:** quit → rejoin same UUID near last feet; Creative mode survives reconnect. Human Gate A for tags.

**Non-goals:** Mojang `player_*` NBT; `players/` Docker volume; Health/Hunger persist; mid-tick Saves; death-position restore; multi-world playerdata.

### 61. Dual storage — ZLDB default + Mojang-compat seam (H1#8 direction)

**Choice:** Keep **two layers** separate forever:

1. **Engine / on-disk format** — Zenith product worlds stay on **`Zenith.LevelDB` (ZLDB)** (ADR §11: RAM SortedDictionary + WAL + one snapshot). Do **not** replace ZLDB with a full Mojang/Vedrock LSM on the LAN hot path.
2. **World key schema** — Zenith keys (`c:` / `ov:` / `ct:` / `inv:` / `pd:`) vs Mojang BDS keys (`subchunk` / `player_*` / …) are different products. Never mix both schemas in one DB “and hope.”

Gameplay / `World` talk only to **`IChunkStorage`**. Backends:

| Backend | Role |
|---------|------|
| `InMemoryChunkStorage` | tests |
| `LevelDbChunkStorage` → ZLDB | **default** ops path |
| Future `MojangChunkStorage` (name TBD) | optional: open a BDS world folder via a **Mojang-shaped** LevelDB leaf (zlib LSM — e.g. binding/`df-mc/goleveldb` lineage, Vedrock `vlang/leveldb` as study ref). Not a rewrite of `libs/leveldb`. |

**Conversion (second product):** an **offline** CLI / one-shot tool BDS → Zenith world (ZLDB + Zenith keys). Runs outside GameLoop. Result is a native Zenith world, not a hybrid. Live write-back to Mojang folders is Deferred until import read-path is honest.

**Ops honesty (§20):** `zenith.yml` must make the mode explicit (e.g. storage kind / path semantics). Never silently reinterpret a ZLDB dir as BDS or the reverse. Warn loudly on mismatch.

**Why:** Users who want Mojang fidelity should plug a real Mojang-compat LevelDB behind the seam — not force Zenith’s perf/DX store to become RocksDB-theatre. Vedrock’s LevelDB (zlib, goleveldb-derived) is a **reference** for engine A; Dragonfly/PNX/PM show softcore `players/` splits we deliberately avoid for native data (§60). Dual-path + converter beats “one engine to rule them all.”

**Spike order (when implementing):** (1) freeze what `IChunkStorage` must expose for a read-mostly Mojang backend, (2) leaf spike: open one BDS folder, read spawn column / one key family, (3) offline converter MVP → reopen under ZLDB, (4) only then live Mojang write-path.

**Refs (not in-repo):** `~/Development/references/bedrock/Vedrock`, `vlang-leveldb`, `goleveldb-mcpe` (`df-mc/goleveldb`).

**Non-goals this ADR:** shipping the Mojang backend or converter in the same PR as the decision; noise/biome gen (separate H1#8 product choice); replacing ZLDB; silent path migrators; multi-world load framework.

**Status:** Decision recorded (jul 2026). Implementation = future PRs under H1#8.

### 62. World domain map — Zenith-native seams (no BDS)

**Choice:** Document World as a **façade** over named subdomains; keep files flat under `src/zenith/World/` until a later ADR forces folder cuts. Add two seams only:

1. **`ITerrainProvider`** — base column payload (+ matching `SampleBaseBlock` for SoftCap / GetBlock). Default = `FlatTerrainProvider` (`ChunkPayloads.BuildFlatOverworld`). Future noise/gen plugs here — **not** via Mojang storage (§61).
2. **`WorldStorageKeys`** — SSOT for Zenith KV prefixes (`c:` / `ov:` / `ct:` / `inv:` / `pd:`). Reserve Mojang namespaces (`player_`, `player_server_`, `~local_player`) — never write them from Zenith backends.

| Subdomain | Owns | Today |
|-----------|------|--------|
| Terrain base | Column when storage miss / heal | `ITerrainProvider`, `ChunkPayloads` |
| Overlay grid | Sparse permanent edits + SoftCap | `World` `_blockOverrides` / `_overlaysByChunk` |
| Persistence port | Put/Get/Flush | `IChunkStorage`, `LevelDbChunkStorage`, InMemory |
| Block registry | Palette + curated + profiles | `Blocks`, palettes, `DigProfiles`, `Tools`, `BreakDuration` |
| Containers | Chest RAM + pairing | `ChestStore`, `ChestPairing`, `ChestFacing` |
| Floor / gravity | Cell stores | `FloorDropStore`, `GravityPendingStore` |
| Packed blobs | On-disk formats | `SlotBlob`, `PlayerDataBlob` |

**Explicit:** `inv:` / `pd:` hydrate & Persist stay thin methods on `World` (same DB) — no `PlayerStore` type yet. Gameplay decides; Protocol transmits; Packets serialize.

**Why:** H1#8 needs a clear native World architecture before BDS. Flat-baked into `World` blocked gen without a storage rewrite. Folder explosion / `Item/` cut still deferred (§55).

**Non-goals:** BDS backend/converter (§61); noise product; `World/Terrain/` subfolders; `Item/` cut; rewriting overlays into subchunks. (**Dimension type** → shipped as seam in §71 — not multi-dim product.)

**Status (jul 2026):** Shipped — ADR + `WorldStorageKeys` + `ITerrainProvider` / `FlatTerrainProvider`. Product terrain gen → §63. Dimension seam → §71.

### 63. Noise heightmap terrain (H1#8 product)

**Choice:** First native gen after §62 seams — **seeded hash heightmap**, not biomes/caves/BDS.

1. **`OverworldTerrainSampler`** — shared surface Y + stone/grass/air rules; `SampleBlock` must match column build.
2. **`ChunkPayloads.BuildOverworldColumn`** — paletted subchunks from a world-space sampler (flat reuses it).
3. **`NoiseTerrainProvider`** — implements `ITerrainProvider`; per-chunk columns on miss (§45 no Put).
4. **Config:** `world.terrain: flat | noise`, `world.seed` (default `1`). Boot log prints mode.

| Mode | Provider | Surface |
|------|----------|---------|
| `flat` (default) | `FlatTerrainProvider` | constant Y -61 |
| `noise` | `NoiseTerrainProvider` | §63 hills → **§64** full overworld band + features |

**Storage interaction:** Existing `c:` blobs with valid subchunk v8 header still win (§45). Switching terrain on a populated world does not rewrite disk — operator creates fresh world folder or clears `c:` keys.

**Why:** H1#8 product step with smallest leaf surface; proves §62 seam before biomes or §61 import.

**Non-goals:** biomes, caves, ore, structures, multi-subchunk product beyond what hills need; persisting generated `c:` on miss; Mojang folder layout.

**Status (jul 2026):** Shipped — noise provider + config + leaf tests. Expanded band/features → §64.

### 64. Overworld band + surface features (trees, water, caves, ruins)

**Choice:** Grow `noise` mode toward a Bedrock-shaped overworld **without** a biome engine or BDS gen.

1. **Height band:** surface ≈ Y 40–88 (base 64 ± hills), floor Y -64 bedrock, deepslate below Y 0, stone above.
2. **Sea:** `SeaLevel = 62` — air between surface and sea fills with water (lakes/coast).
3. **Caves:** deterministic 3D hash carve under surface (not noise libraries).
4. **Trees:** oak log + leaves on a cell grid (~22% of 10×10 cells), dry land only.
5. **Ruins:** rare 5×5 cobble pads + plank ring (structure placeholder — not villages).
6. **Spawn:** climb clear air above `max(surface, sea)` so trees/water never bury join/respawn.

Flat mode **unchanged** (classic Y -61). Features live only in `SampleNoiseBlock` / `NoiseTerrainProvider`.

**Why:** H1#8 “world beyond flat” needs playable vertical space and visible variety before biomes.

**Non-goals:** biomes, ore veins, villages/strongholds, mob spawning, structure NBT, liquid flow simulation, rewriting stored `c:` columns.

**Status (jul 2026):** Shipped — sampler expansion + Blocks (leaves/bedrock/water/cobble/deepslate) + tests.

**Adendo (jul 2026 — continuity):** Cross-chunk tree canopy: `BuildNoiseOverworldColumn` raises `maxWorldY` from neighbor canopy (`MaxTreeCanopyYAffectingChunk`) so leaves are not truncated at chunk borders. **Height algorithm** → superseded by Simplex in §71 (was 8×8 bilinear + blended biome bias).

### 65. Worm cave carvers (H1#8 — caves v2)

**Choice:** Replace §64 hash “Swiss cheese” with **deterministic worm segments** + rare deep **cheese spheres**.

1. **`OverworldCaveCarver`** — per chunk 2–4 worms (48–128 steps, turning path); radius by depth (1 shallow → 3 deep); ~1/9 chunks get a deep cheese sphere.
2. **`OverworldCaveContext`** — precomputes segments for 3×3 chunk neighborhood once per column build (perf); point `SampleBaseBlock` uses live query (same geometry).
3. **Guards:** no carve at `y >= surface - 4` or bedrock floor; features after fill+carve.
4. **Flat** unchanged.

**Why:** Playtest + design review — hash caves were not explorable; worms match vanilla *shape* without BDS carver tables or noise libs.

**Non-goals:** aquifers, lush/dripstone biomes, carver types (ravine, nether), persisting `c:` on miss.

**Status (jul 2026):** Shipped — worm carver + context + leaf tests.

### 66. Ore veins (H1#8 — underground resources)

**Choice:** Deterministic **8×8×8 cell clusters** (Chebyshev radius 2) replacing stone/deepslate hosts only.

1. **`OverworldOrePlacer`** — ~1/7 cells spawn a vein; ore type by Y band (coal→copper→iron→gold→redstone→lapis→diamond); deepslate variants when host is deepslate (Y &lt; 0).
2. **Sampler order:** caves → terrain layers → ore replace → surface features unchanged.
3. **`Blocks` + `DigProfiles`:** overworld + deepslate ore set; pickaxe harvest; break drops ore block item (no fortune/smelting yet).

**Why:** Playable survival mining before biomes; smallest leaf that does not need loot tables or feature JSON.

**Non-goals:** fortune/silk touch loot, ore smelting recipes, nether/emerald/quartz, biome-scaled rates, persisting `c:` on miss.

**Status (jul 2026):** Shipped — ore placer + blocks + leaf tests.

### 67. Coarse overworld biomes (H1#8 — biome v1)

**Choice:** **48×48** deterministic biome regions — no noise libs, no BiomeDefinitionList payload.

1. **`OverworldBiomeSampler`** — ocean / plains / desert / hills / forest; Bedrock network ids (0–4); height bias + surface block (sand vs grass) + tree density.
2. **Column wire** — `ChunkPayloads` writes sampled biome id per subchunk section (center of chunk).
3. **StartGame** — `ITerrainProvider.SampleSpawnBiome` → `BiomeType` / `BiomeName` at join (flat stays plains).
4. **Flat** unchanged (plains biome + grass column).

**Why:** Visible variety + correct client biome tint/F3 without a definition dump or BDS tables.

**Non-goals:** biome JSON, 3D biome blending, taiga/swamp/jungle, mob spawn rules, **custom** BiomeDefinitionList authoring, persisting `c:` on miss. (Embedded vanilla list for join is §70 — not this product.)

**Status (jul 2026):** Shipped — sampler + column wire + spawn biome + leaf tests.

### 68. Pterodactyl / Wings hosting (egg + panel hooks)

**Choice:** Pterodactyl is a **platform adapter** on the **same product image** as Compose/Dokploy (ADR §20 unified-image adendo) — egg JSON + env, not a second Dockerfile.

1. **Data root:** Wings server files live in **`/home/container`** → egg/startup sets `ZENITH_DATA=/home/container` (Compose uses `/data`).
2. **Image:** [`deploy/image/Dockerfile`](../deploy/image/Dockerfile) — binary `/opt/zenith`; entrypoint seeds config and optionally evals Wings `${STARTUP}`.
3. **Egg:** [`deploy/pterodactyl/egg-zenith.json`](../deploy/pterodactyl/egg-zenith.json) — yaml parser on `zenith.yml` for allocation port; `startup.done` = **`Server ready.`**; install seeds shared [`deploy/zenith.yml`](../deploy/zenith.yml).
4. **Code hooks (panel conventions only):**
   - **`SERVER_PORT`** env overrides `server.port` after yaml load (`ServerConfigOverrides`).
   - **`Server ready.`** log after UDP bind (`RakNetServer.OnListening`).
   - **`server.guid`** under `ResolvePersistentRoot()` (not image dir).
5. **No extra `ZENITH_*` vars** beyond `ZENITH_DATA` + Wings `SERVER_PORT`.

**Why:** Hosting panels are a distribution channel; one image + one yaml SSOT keeps Dokploy and Wings from drifting.

**Non-goals:** official GHCR publish in this ADR (operators build/push locally until tagged); Pterodactyl-specific gameplay; auto-reload config without restart; a second product Dockerfile under `deploy/pterodactyl/`.

**Status (jul 2026):** Shipped — unified image + egg adapter + hooks + docs.

### 69. Noise column gen — spatial caves + single-pass payload (join perf)

**Choice:** Keep §65–§67 **geometry** (same hashes / worms / ores / biomes); speed PreSpawn via data-oriented column build.

1. **`OverworldCaveContext` XZ CSR grid** (8³ cells over 3×3 chunk neighborhood) — carve tests scan nearby segments only; brute-force path kept for leaf equality tests.
2. **`ChunkPayloads.BuildNoiseOverworldColumn`** — stack `FillColumnSurfaces` (256 surface Y + biome), single-pass sections to `max(surface+8, sea)`, `ArrayPool` section buffer, linear stack palette (no `Dictionary`).
3. **`SampleNoiseBlockAtSurface`** — column path reuses cached surface/biome; point `SampleBaseBlock` unchanged semantically.
4. **PreSpawn** still parallel `GetRadiusAsync` (§14 adendo).

**Why:** Linear segment scan × every underground block made radius-4 join ~50s+; spatial + no double-sample unlocks smoke join budget.

**Non-goals:** Changing worm counts/lengths, noise libs, biome blending, persisting `c:` on miss, SIMD mandatory.

**Status (jul 2026):** Shipped — cave grid + noise column builder + budget test.

### 70. Join terrain contract (client-ready)

**Choice:** Hybrid **ready-disk** join — not DF spawn-first with zero LevelChunks, not a full view-radius dump before `PLAYER_SPAWN`.

Client leave-loading prerequisites (wire order SSOT; no `JoinOrchestrator`):

1. **Pose gate** — `World.TryHealSpawnFeet` before StartGame (buried flat `pd:` into noise must not trap the camera).
2. **Non-empty `BiomeDefinitionList`** — embedded vanilla wire body (`data/biome_definitions.bin`); not a custom biome-JSON product (§66 non-goal stays for *authoring*).
3. **`PLAYER_SPAWN` only after a ready disk** centered on feet: `world.spawn-ready-radius` (default **2**, validate `0..spawn-chunk-radius`). PreSpawn loads/publishes `min(view, spawn-ready-radius)`; tracker view radius stays the client request (capped by `spawn-chunk-radius`).
4. **One column pipeline** — `ColumnSend` + `PlayerChunkTracker` + backpressure for ready-disk and the remaining ring. `ChunkStreamSystem` runs while `Player.IsSpawning` **or** `IsInGame` (stream during SpawnResponse, DF-style terrain-after-spawn-protocol).
5. Inventory seed + MovePlayer teleport before `PlayStatus(PLAYER_SPAWN)` (PM-shaped).

**Clarifies:** §14 PreSpawn/stream adendos; §66 “no BiomeDefinitionList payload” applied to *custom* biome dumps — embedded vanilla list is join contract, not biome product.

**Non-goals (next ADR):** `SubChunkRequestModeLimited` + SubChunk handler; `PLAYER_SPAWN` with zero LevelChunks; AvailableCommands / mid-session gamemode UI; client-side gen / full biome JSON authoring.

**Status (jul 2026):** Shipped — config + embedded biomes + `IsSpawning` stream gate.

### 71. Dimension seam + Simplex overworld (PM-inspired)

**Choice:** Introduce a minimal **Dimension** domain type (Serenity/DF-shaped) and attach PM-inspired **Simplex** height/biomes to the overworld generator — without copying Serenity Entity/Feature maps or PM populate-with-adjacents.

1. **`Dimension`** — `Identifier`, `WireId` (= `Packets.DimensionId`), `ITerrainProvider Terrain`. `World` owns default **overworld**; LevelChunk / `ChunkColumnData.DimensionId` come from `World.Overworld.WireId`.
2. **`SimplexNoise`** leaf under `World/Noise/` (2D + octaves; no NuGet) — PM `Simplex`/`Noise` shape.
3. **Height** — octaved Simplex × `NoiseHillAmplitude` + biome bias (replaces 8×8 hash lattice).
4. **Biomes** — temp/rainfall Simplex → lookup mapped onto Ocean/Plains/Desert/Hills/Forest (replaces 48×48 hash cells).
5. Worms / ores / trees / column pipeline / join contract (§70) unchanged semantically.

**Supersedes:** §62 non-goal “Dimension type” (seam only — not multi-dim product). Clarifies §63–§67 height/biome algorithms.

**Why:** Wire already has DimensionId; gen/storage/spawn must not stay forever glued to an implicit mono-world. Better relief needs real noise, not hash lattices. Serenity proves World→Dimension→Generator; PM Normal proves Simplex climate+height.

**Non-goals:** Nether/End Dimensions; Serenity chunk GC / Entity maps / DimensionFeature; PM `PopulationUtils` 3×3 populate; noise NuGet; rewriting stored `c:` columns.

**Status (jul 2026):** Shipped — Dimension + Simplex overworld + leaf tests. **Noise leaf** → superseded by Auburn FastNoiseLite in §72 (height composition also hardened).

### 72. FastNoiseLite leaf + anti-pillar height (composition)

**Choice:** Vendor Auburn **FastNoiseLite** (single MIT `FastNoiseLite.cs` under `World/Noise/` — no official NuGet) as the overworld noise leaf, and fix height **composition** that produced 1×1 pillars.

1. **Leaf** — OpenSimplex2 + FBm via FastNoiseLite; Zenith configures seed/frequency/octaves only (`OverworldNoiseFields`). Delete homemade `SimplexNoise`.
2. **No per-block hash on height** — remove `Hash(x,z)%N` jitter from surface Y.
3. **Continuous climate bias** — `ContinuousHeightBias(temp, rain)` with smoothstep weights (hills/desert/ocean) so discrete biome flips do not cliff ±10–15.
4. **Gentler hills** — height frequency `1/128`, 3 octaves, amplitude 22; continuity contract `MaxAdjacentSurfaceStep = 6` (enforced by leaf test).
5. No hard ocean `Min(sea-1)` — ocean depth comes from continuous bias only (hard clamp was the cliff source).

**Supersedes:** §71 homemade Simplex leaf + discrete/hash height bias. Keeps Dimension seam and five biome kinds.

**Why:** Playtest showed pleasant macros with local “random columns.” Root cause was composition (hash jitter + hard biome bias), not missing a NuGet. FastNoiseLite is the maintained portable leaf; ADR records “do not invent another Simplex.”

**Non-goals:** NuGet wrapper packages (VL.* etc.); domain warp / 3D density terrain; Nether/End; rewriting stored `c:` columns; PM 3×3 populate.

**Status (jul 2026):** Shipped — vendored FNL + continuous bias + continuity test.

### 73. Floor-drop honesty — Q-throw + death loot

**Choice:** Close the Survival drop holes without WorldEntity/ECS.

1. **Q-throw / ISR Drop** — `InventorySystem.TryDrop` deposits into `FloorDropStore` + `AddItemActor` fan-out (`FloorDropFanout`) at the player feet cell; pickup delay **40** ticks (~2s) so the thrower does not instantly re-collect. SoftCap refuse → ISR rollback (item stays in bag).
2. **Death loot** — Survival void death dumps craft UI + bag + cursor to floor near death XZ (Y clamped to surface when below `FlatMinY` so void loot is recoverable). Creative = keepInventory. SoftCap may void remainder (Debug).
3. **Break** — unchanged: TryAdd into inventory first; surplus → floor (already §26). Multi-id chest dump uses spiral neighbor cells (one StackId per cell).
4. **Store** — `TryAddOrMerge` refuses overwrite when a cell holds a different id (was silent clobber).

**Clarifies:** §26 Deferred Q-throw; §40 “inventory unchanged on death.”

**Non-goals:** Gravity/despawn TTL; LevelDB persist of drops; XP orbs; damage pipeline deaths beyond void; always-floor-on-break (vanilla bag-first stays). SoftCap refuse on death → keep slot (§74).

**Status (jul 2026):** Shipped — Fanout + TryDrop + death dump + leaf tests.

### 74. Dig/chest honesty — wrong-tool no-drop + Creative chest dump

**Choice:** Close the two LAN “looks like a bug” gaps left after §73 without opening vitals/TTL/Destroy.

1. **Wrong-tool no-drop** — Survival break still removes the cell when dig auth passes. If `DigProfiles.RequiresCorrectToolForDrops` and `!BreakDuration.IsHarvestable(held)`, skip bag/floor for the **broken block rid**. Soft blocks / chest block item still drop with empty hand. Chest **contents** dump unchanged.
2. **Creative chest contents** — InstantBuild still skips dropping the chest **block**. `RemoveAndDump` contents always go to `FloorDropFanout` (Creative does not TryAdd to bag). Clarifies §31 “clears chest store” ≠ void.
3. **Death SoftCap** — `DumpOnDeath` clears a slot only after successful floor deposit; SoftCap refuse keeps the stack in bag (Debug). Break SoftCap-after-air remains documented nit (no rollback this ADR).

**Clarifies:** §27 Deferred wrong-tool loot; §31 Creative break; §73 SoftCap death void.

**Non-goals:** Vanilla loot tables (cobble from stone); tool durability; Creative Destroy ISR; floor despawn TTL; break SoftCap world rollback.

**Status (jul 2026):** Shipped — loot gate + Creative chest dump + death SoftCap keep + leaf tests.

### 75. Cave context alloc — ThreadStatic scratch + ArrayPool

**Choice:** Cut the ADR §69 cave-context GC spike without changing carve semantics.

1. **`OverworldCaveContext`** becomes a disposable class (was readonly struct). Column path / point `IsCarved` use `using`.
2. **Collect** into a **ThreadStatic** `List<CaveSegment>` (clear between columns; parallel `GetRadiusAsync` safe).
3. **Segments + CSR indices** via `ArrayPool<T>.Shared` sized to exact count (no `List(4096)` + `ToArray` double buffer).
4. Bucket offsets stay a tiny `new int[37]` per build (negligible vs segment buffer).

**Why:** ShortRun showed `Noise_CaveContextOnly` ~190 KB Allocated with Gen2 — dominated by capacity-4096 list + ToArray. Encode was already cheap; join budget OK; next leaf was alloc honesty.

**Non-goals:** Persisting `c:` on miss; pooling the final LevelChunk payload `byte[]`; changing worm counts/lengths; SIMD.

**Status (jul 2026):** Shipped — pooled cave context + dispose on column/bench paths + leaf test.

## Explicit non-goals (so far)

Recorded so we don't “accidentally” implement them:

- Plugin API / DI container
- `/` command **framework** + permissions / autocomplete (single `/gamemode` path is §52 — not a framework)
- **Replacing** ZLDB with full Mojang LSM on the default path (Mojang open/import → §61 seam + converter instead)
- Multi-level LSM compaction **inside** `Zenith.LevelDB` / PInvoke RocksDB as the product store (unless RAM/streaming need is proven — still not “become BDS”)
- Full creative catalog / `block_state_b64` decode (short CreativeContent list shipped in §31)
- WorldEntity / ECS / mob AI (drop-entity **wire** shipped without a generic entity layer — §32)
- Protocol bump solely to chase client log version numbers when login already completes
- Actor/EventHandler frameworks copied from other engines

When a non-goal becomes a goal, update this file **and** `ARCHITECTURE.md`.

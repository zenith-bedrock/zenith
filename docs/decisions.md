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

**Why:** H1-3 LAN need — Survival↔Creative in-session without restart/`zenith.yml`. Modes/abilities already exist (§31/§37). At this historical point, one string parse beat a command framework (`dx.md` freeze). The command-freeze portion is superseded by §100; its narrow implementation rationale remains historical.

**Deferred:** Adventure/Spectator; permissions; Bedrock `AvailableCommands` / autocomplete; `/` registry.

**Historical status:** this narrow `/gamemode` decision is superseded for command-surface policy and Bedrock metadata by ADR §100; its original scope and smoke evidence remain historical.

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
3. Freeze list unchanged at this historical milestone: no Scheduler, Actor/ECS, VisibilitySystem, DI, plugin API, `/` command framework, `Network/` revival. The command-framework portion is superseded by §100; the remaining freezes stand.
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

**Addendum (ago 2026 — §98):** death loot is now one planned batch. If the complete craft-grid + bag + cursor set cannot fit, no floor drop is committed and no source slot is cleared; the dead player retains all of it for the respawn inventory resync. This replaces the old per-slot "keep the remainder" behavior.

**Clarifies:** §26 Deferred Q-throw; supersedes §40's earlier “inventory unchanged on death” behavior for Survival.

**Non-goals:** Gravity/despawn TTL; LevelDB persist of drops; XP orbs; damage pipeline deaths beyond void; always-floor-on-break (vanilla bag-first stays). Death SoftCap refusal retains the complete source set (§98).

**Status (jul 2026):** Shipped — Fanout + TryDrop + death dump + leaf tests.

### 74. Dig/chest honesty — wrong-tool no-drop + Creative chest dump

**Choice:** Close the two LAN “looks like a bug” gaps left after §73 without opening vitals/TTL/Destroy.

1. **Wrong-tool no-drop** — Survival break still removes the cell when dig auth passes. If `DigProfiles.RequiresCorrectToolForDrops` and `!BreakDuration.IsHarvestable(held)`, skip bag/floor for the **broken block rid**. Soft blocks / chest block item still drop with empty hand. Chest **contents** dump unchanged.
2. **Creative chest contents** — InstantBuild still skips dropping the chest **block**. `RemoveAndDump` contents always go to `FloorDropFanout` (Creative does not TryAdd to bag). Clarifies §31 “clears chest store” ≠ void.
3. **Death SoftCap** — superseded by §98's all-or-nothing death-loot plan. Break SoftCap-after-air remains documented nit (no rollback this ADR).

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

### 76. Packet wire codegen — `[GamePacket]` source generator

**Choice:** Mechanize `DataPacket.Encode`/`Decode` boilerplate with a Roslyn **incremental source generator** (`zenith.PacketGenerator`, netstandard2.0, referenced as `OutputItemType="Analyzer"`) driven by attributes on `partial class` packets — not runtime reflection, not a DI/mapper abstraction.

1. **Opt-in per class** — `[GamePacket(protocolId)]` on a `sealed partial class : DataPacket`. No attribute → generator does not touch the class; today's hand-written packets are untouched unless explicitly migrated.
2. **Attributes** (`src/zenith/Packets/Generation/WireAttributes.cs`): `[Wire(Endianess)]` fixed-width, `[WireVar]` LEB128/zigzag, `[WireString]`, `[WireByteArray]`, `[WireUuid]`, `[WireNested]`/`[WireNestedArray]` (reuse the existing `static T Read(ref BinaryStream)`/`void Write(ref BinaryStream)` convention already used by `SerializedSkin`/`CommandOriginData` — no new `IWireSerializable`/`static abstract` interface introduced), `[WireOptional]` (has-value-flag pattern), `[WireWhen(nameof(Other), value)]` (conditional presence, e.g. `MovePlayerPacket`-style teleport-only fields), `[WireIgnore]`.
3. **Wire order = declaration order.** No `Order` property — matches every hand-written packet today and removes an entire "attribute says X, declaration says Y" bug class.
4. **Generated code is textually what a careful human writes today** — same `BinaryStream` calls, same `if/else` shape for optional/conditional fields, no boxing/closures/helper indirection. `Id` is generated too (from the attribute's constructor arg).
5. **Diagnostics** `ZPG001`–`ZPG013` (missing `partial`, wrong CLR type for an attribute, `[WireNested]` element missing `Read`/`Write`, `[WireWhen]` referencing an undeclared/out-of-order property, duplicate protocol IDs, etc.) computed from the semantic model at build time.
6. **Not every packet migrates.** Packets with NBT/JSON blobs, unbacked literal placeholders, deliberately asymmetric encode/decode, discard-on-decode reads, or a scaled/derived wire value (not a raw field) stay 100% hand-written forever (Tier B), not force-fit: `StartGamePacket`, `LoginPacket`, `MovePlayerPacket`, `InventoryTransactionPacket`, `MobEquipmentPacket`, `CraftingDataPacket`, `ItemStackRequestPacket`/`ItemStackResponsePacket`, `InteractPacket` (discards target-actor-runtime + optional position), `PlaySoundPacket` (position is `int(value * 8)` BlockPos scaling, not a raw field), `EmoteListPacket` (`Guid[]` array + a decode-time cap-exceeded throw — business validation, not mechanical), `PlayerAuthInputPacket` (7-bit-per-byte input bitset decode + several embedded skip-only sub-decoders — pure control flow, no field list), `PlayerSkinPacket` (its `Uuid` property is `string` but the wire type is `Guid` — `ToString("D")`/`Guid.Parse` conversion, not a raw field; would need a type change across 3 call sites to migrate honestly, deferred) (list may grow during migration; each addition is a plain code-review call, not a new ADR).

By ago 2026, 31 of 64 packet types have migrated to `[GamePacket]`: the 23 from the first pass (`SetTimePacket`, `ModalFormResponsePacket`, `DisconnectPacket`, `CommandRequestPacket`, `SetPlayerGameTypePacket`, `RequestChunkRadiusPacket`, `RequestAbilityPacket`, `SetLocalPlayerAsInitializedPacket`, `NetworkSettingsPacket`, `ContainerClosePacket`, `ToastRequestPacket`, `StopSoundPacket`, `EmotePacket`, `PlayerActionPacket`, `ServerSettingsResponsePacket`, `RequestNetworkSettingsPacket`, `ResourcePackClientResponsePacket`, `RespawnPacket`, `SetTitlePacket`, `ModalFormRequestPacket`, `BlockEventPacket`, `AnimatePacket`, `LevelSoundEventPacket`) plus 8 more (`UpdateBlockPacket`, `ChunkRadiusUpdatedPacket`, `ContainerOpenPacket`, `LevelEventPacket`, `RemoveActorPacket`, `TakeItemActorPacket`, `PlayStatusPacket`, `UpdateAdventureSettingsPacket`). What remains unmigrated is the Tier B list above plus the ~29 outbound-only packets whose hand-written `Decode` is an intentionally empty no-op (migrating those would make Decode do real work it was deliberately never given, which is exactly the kind of "generator decides more than the human wrote" this ADR rules out — left as-is).

**`[WireVar(unsigned: true)]` added:** `UpdateBlockPacket` exposed a real gap in the original `[WireVar]` design — `BlockRuntimeId`/`Flags`/`DataLayerId` are `int`-typed (matching every other block-runtime-id/flags field in the codebase, e.g. `WorldProtocol.SendUpdateBlock`) but are wire-encoded via `WriteUnsignedVarInt`/`ReadUnsignedVarInt` directly, with **no cast** - `BinaryStream`'s Unsigned* methods already take/return plain `int`/`long`. The original design only had two `[WireVar]` outcomes for an `int` property: zigzag `VarInt` (the default) or requiring a CLR type change to `uint` (rippling into every call site that already treats the value as `int`, which is most of them). `[WireVar(unsigned: true)]` lets an `int`/`long` property opt into `UnsignedVarInt`/`UnsignedVarLong` without changing its CLR type - see `WireVarAttribute`'s doc comment in `WireAttributes.cs` and `GamePacketGenerator.ResolveVarMethod`. Backward compatible (defaults `false`, matching every previously-migrated packet's existing zigzag behavior).

**Cross-checked against the official Mojang branch, not just Endstone:** `protocol-import pull --source mojang --ref "r/26_u4"` (the branch `readme.md` itself names as Zenith's reference, distinct from `main` which tracks the newest Mojang docs tip) confirmed most of the batch matches field-for-field (modulo the expected human-readable-vs-compact naming difference already seen with Endstone). Three packets showed **real structural drift** between `r/26_u4` and what protocol 1001 (Zenith's locked target, per `readme.md`) actually implements: `UpdateBlockPacket`'s `X`/`Y`/`Z` are grouped into a single `BlockPosition` in the newer schema; `ContainerOpenPacket` gained a `TargetActorId` field and renamed several others; `UpdateAdventureSettingsPacket`'s 5 separate bools collapsed into one combined `AdventureSettings` field. None of this changed anything here - the migrations preserve the exact wire format the hand-written code already had (verified byte-identical generated output before/after) - but it's a concrete, reviewable signal for whoever eventually plans a protocol version bump (`docs/protocol-churn.md`), which is exactly the payoff `protocol-import diff` was built for.

**Why:** 64 packet types, most of them straight-line field-by-field `WriteX`/`ReadX` mirrored by hand between Encode and Decode — pure mechanical boilerplate with no domain decision in it, and a real source of Encode/Decode desync bugs on edit. A compile-time generator removes the toil with zero runtime cost and zero new allocations (verified per-packet against `zenith.Benchmarks`' `GamePacketBenchmarks` `[MemoryDiagnoser]` baseline before/after migration) — it mechanizes existing wire logic, it does not decide or transmit anything, so it does not cross into Protocol/Gameplay territory.

**Clarifies:** `ARCHITECTURE.md` §7's freeze list (Scheduler/Actor/ECS/DI/plugin API/etc.) is about *runtime* abstractions; a compile-time source generator emitting the same code a human would type is a different category and does not need to join that list — but any future proposal to also generate the packet-dispatch `switch` statements in session handlers would cross into Handler orchestration and needs its own ADR (see Non-goals).

**Non-goals:** Generating the inbound packet-dispatch `switch` statements across `Session/Handler/*SessionHandler.cs` (different architectural layer — Handler orchestration, not wire serialization; risks becoming the "Dispatcher" abstraction already on the freeze list — needs its own ADR if pursued); an `IWireSerializable`/`static abstract`-interface-based nested-object contract (the existing static-factory convention is kept); forcing Tier B packets into attributes; runtime reflection-based (de)serialization.

**Future work (not this ADR, recorded so intent isn't lost):** a standalone offline import tool (e.g. `tools/protocol-import`, no network calls during build, not run in CI) that scaffolds `[GamePacket]`-attributed stubs from Mojang's `bedrock-protocol-docs` JSON Schema dumps (`x-underlying-type`/`x-serialization-options: ["Compression"]`/`x-ordinal-index`/`$ref`/`required` map directly onto `[Wire]`/`[WireVar]`/`[WireNested]`/`[WireOptional]` — verified against real files in that repo) and/or EndstoneMC's `protocol-docs`/`protocol-dumper` output, plus a diff mode to flag drift when Minecraft bumps its protocol version. Output is always human-reviewed, never auto-merged. Mojang's schema did not show how conditional presence (`[WireWhen]`) is expressed at the schema level in the samples checked — open question for whoever builds this.

**Addendum (ago 2026) — `protocol-import` shipped, two sources:** the future-work import tool above is built (`tools/protocol-import`, Spectre.Console.Cli), with `pull`/`scaffold`/`diff`/`list` commands and an `ISchemaSource` seam behind a `--source` selector. Both candidate sources ship: `EndstoneSchemaSource` (flat, clean per-packet JSON) and `MojangSchemaSource` (official, JSON-Schema-with-`$ref` shape — confirmed by pulling and parsing real files, not assumed). Two facts worth recording for whoever touches the importer next:

1. **Field order is `x-ordinal-index`, not JSON property declaration order.** Confirmed directly on `CommandRequestPacketPayload.json`: properties are declared `Command, IsInternal, Origin, Version` in the file but their ordinal indices are `0, 2, 1, 3` — `Origin` is actually wire field #1. Sorting by declaration order silently scaffolds the wrong wire order.
2. **`required` means something different in a packet-payload file vs. a standalone type file.** `CommandRequestPacketPayload.json` (a payload) requires all 4 of its fields — none optional. `CommandOriginData.json` (a *type*, referenced via `$ref`) only requires `Type`/`UUID`, yet the real hand-written `CommandOriginData.Read` unconditionally reads all 4 fields with no has-value-flag anywhere — there, `required` absence just means "has a JSON-Schema default value for tooling," not "wire-optional." The importer's rule: `required` only drives `[WireOptional]` at the packet-payload level; type files are always parsed as all-fields-always-present.

The `[WireWhen]` open question from above is still open on the Mojang source too — `MovePlayerPacketPayload.json`'s `Teleport Data` field says "Present only when Position Mode is Teleport" in its `description` (prose, not machine-readable), same as Endstone's plain `optional: true`. The importer still never guesses a `[WireWhen]` condition; it always scaffolds these as `[WireOptional]` for a human to upgrade if warranted.

**Status (ago 2026):** Shipped — generator + attributes + diagnostics + Phase 1 proof-of-concept migrations + `zenith.PacketGenerator.Tests` + `protocol-import` CLI with Endstone and Mojang sources + `protocol-import.Tests`.

**Addendum (ago 2026) — migration audited, `TextPacket` classified Tier B, no plain candidates remain:** re-checked all 50 still-unmigrated files in `src/zenith/Packets/`. 40 are outbound-only packets with an intentionally empty `Decode` — already covered by the "left as-is" note above. Of the 10 with real `Decode` logic, 9 (`EmoteListPacket`, `InteractPacket`, `InventoryTransactionPacket`, `ItemStackRequestPacket`, `LoginPacket`, `MobEquipmentPacket`, `PlaySoundPacket`, `PlayerAuthInputPacket`, `PlayerSkinPacket`) were already named Tier B above. The 10th, `TextPacket`, was unclassified — on inspection it belongs in Tier B too: `Encode` writes a derived `category` byte (`CategoryFor(Type)`) that `Decode` reads and discards (`stream.ReadByte(); // category`), a discard-on-decode/asymmetric pattern; and its field list is a 3-way switch on `Type` (`TypeChat`/`TypeWhisper`/`TypeAnnouncement` vs `TypeTranslation`/`TypePopup`/`TypeJukeboxPopup` vs default), not a single-condition `[WireWhen]`. Forcing it into attributes would be exactly the "forcing Tier B packets into attributes" non-goal above. **No further plain (no-ADR) packet migrations remain** — the 31/64 figure is the practical ceiling for the current attribute set; growing it further needs either a multi-branch conditional attribute (new ADR) or accepting a wire-format-preserving Encode/Decode asymmetry in the generator (against this ADR's stated design).

### 77. Floor-drop despawn TTL

**Choice:** Close the remaining §73 gap — uncollected floor drops now disappear after a fixed lifetime, matching vanilla's 5-minute item-entity despawn, without a generic entity/TTL layer.

1. `FloorDropStore.DropSlot` gains an `AgeTicks` field (default `0`, optional positional parameter — no call-site churn). `TryAddOrMerge`/`TryTakeUpTo` explicitly carry the existing cell's `AgeTicks` forward; **merging more of the same item onto a pile does not reset its age** (a cell can't be kept alive forever by topping it off).
2. `FloorDropStore.TickDespawn(expired, tickDiff, despawnTicks = DefaultDespawnTicks)` ages every cell once per `BlockSystem` tick (same two-phase scratch-list pattern as `TickPickupDelays`, to avoid mutating `_drops` mid-iteration) and removes + reports cells that cross `DefaultDespawnTicks` (6000 ticks / 5 min at 20 TPS).
3. `BlockSystem.PickupFloorDrops` sends `RemoveActor` for each expired cell to peers who are in-game or already know the chunk (same visibility rule `FloorDropFanout.Publish` uses) — no bag mutation, the item is just gone, as in vanilla.

**Clarifies:** §73 "Non-goals: ... floor-drop despawn TTL" (that gap is now closed); `roadmap.md` Yes-next priority 3.

**Non-goals:** Per-item despawn time variance (vanilla varies this for some items/enchants — out of scope); despawn timer reset on player proximity or re-throw; LevelDB persistence of drop age across restart (drops are already RAM-only per §26); a generic `Entity`/TTL component — this stays a `FloorDropStore`-local concept.

**Status (ago 2026):** Shipped — `AgeTicks` + `TickDespawn` + `BlockSystem` wiring + leaf tests (`FloorDropStoreTests`).

### 78. EventBus first domain consumers — join/leave chat

**Choice:** Prove the `EventBus` seam (§21) holds for real Gameplay consumers, not just login/quit publishers with nobody listening, **before** any plugin surface is discussed. Scope stays inside §21's own boundary: no new event types, no `Unsubscribe`, no priority, no plugin loader.

1. **`PlayerPresenceAnnouncer`** (`Gameplay/PlayerPresenceAnnouncer.cs`) — static, composition-internal, same shape as `FloorDropFanout`. `OnLogin` broadcasts `"{name} joined the game"`; `OnQuit` broadcasts `"{name} left the game"` **only when `PlayerQuitEvent.WasInGame`** — a pre-spawn drop (still in login/resource-pack/spawn) never had a "join" anyone saw, so it does not get a "left" line either. Both use the **existing** `PlayerLoginEvent`/`PlayerQuitEvent` types — §21 explicitly deferred *new* domain event types, not new listeners on the two that already ship.
2. **`PlayerQuitEvent` gains `WasInGame`** (constructor param, not a new type) — `NetworkSession.HandleClose` captures `Player.IsInGame` before it flips to `false`, so the field reflects "did anyone actually see this player" rather than "did a `Player` object exist."
3. **`ChatProtocol.SendSystem(message)`** added alongside the existing `SendChat` — a server-authored line (`TextPacket.TypeSystem`, no `SourceName`) distinct from player-authored chat.
4. **Registration lives in `ZenithServer`** (the composition root), not inside `EventBus` or `PlayerManager` — `eventBus.Subscribe<PlayerLoginEvent>(...)` / `Subscribe<PlayerQuitEvent>(...)` sit next to `gameLoop.Register(...)` calls, same pattern as every other system wire-up.
5. **`EventBusTests.cs`** (new) — the shipped infra had **zero** test coverage until now. Covers: no-op with no listeners, multiple listeners fire in subscribe order, listeners are scoped per event type, a throwing listener does not block later ones (the isolation `Publish`'s doc comment already claimed), and Subscribe-during-Publish only takes effect on the *next* `Publish` call. `PlayerPresenceAnnouncerTests.cs` covers the production wiring: join broadcast excludes the joining player (not yet in-game), quit broadcast excludes the leaving player, pre-spawn drops broadcast nothing.

**Why now:** `comparison.md` already commits Zenith to "extension points sit on Gameplay, not raw packet listeners" as the answer to plugin-API pressure from comparable/newer projects (Basalt in particular — see `roadmap.md` "Competitive read"). That claim was untested — `EventBus` had never been exercised with more than the zero consumers it shipped with. This ADR is the proof, not the plugin API itself.

**Clarifies:** §21 ("Deferred: Domain event types beyond login/quit" — still true, no new types added here); `roadmap.md` Yes-next priority 6.

**Non-goals:** Plugin API / external assembly loading (still frozen, §21); new event types (join/leave chat reuses the two that exist); `Unsubscribe` / listener priority; moving the existing inline `PlayerVisibility.AnnounceLeave` / `ChestLidFanout.ReleaseOpener` cleanup in `NetworkSession.HandleClose` onto `EventBus` — those are ordering-sensitive and out of scope for a small, reversible proof.

**Status (ago 2026):** Shipped — `PlayerPresenceAnnouncer` + `PlayerQuitEvent.WasInGame` + `ChatProtocol.SendSystem` + `ZenithServer` wiring + `EventBusTests` + `PlayerPresenceAnnouncerTests`.

### 79. Protocol bump to 2169 / 1.26.50 — accepted Cereal debt

**Choice:** Bump `ServerIdentity.ProtocolVersion`/`VersionName` from **1001 / 1.26.33** to **2169 / 1.26.50** (`r/26_u4`, the newest branch `protocol-import` can pull) now, accepting that a real 1.26.50 client will desync on a known, documented set of packets until each is migrated — rather than deferring the version bump until every packet is ready.

**Why now, with known gaps:** `protocol-import pull --source mojang --ref r/26_u4` (`changelog_2168_07_07_26.md`) surfaced that Mojang moved **~23 packets to a new "Cereal" serialization scheme** at this protocol — the changelog's own words: *"migrated to Cereal-only serialization. Each is an intentional wire-format change that is not backwards compatible with prior protocols."* This is not a field reorder or a rename; Cereal uses tagged variants and `optional<T>` fields gated by a presence byte, a different shape than the hand-written / `[GamePacket]`-attributed encode Zenith has today. Re-implementing all ~23 correctly is a multi-session effort, not a same-day fix. The product stance (per `readme.md` / `vanilla-behavior.md`) is honest LAN/private use, not silent claims of full compatibility — so the version number moves now, the gap is logged loudly (boot `Warning`, this ADR, `roadmap.md`), and each debt packet closes as its own future ADR-free "just do the Cereal fields" PR (schema is already in `protocol-import`'s cache — no design decision left, just implementation).

**Debt list (Cereal serialization, not yet migrated at 2169):** `StartGamePacket`, `LevelChunkPacket`, `MovePlayerPacket`, `PlayerAuthInputPacket`, `CraftingDataPacket`, `CreativeContentPacket`, `ItemStackRequestPacket`, `ItemStackResponsePacket`, `AddActorPacket`\* , `AddItemActorPacket`, `AddPlayerPacket`, `ClientboundMapItemDataPacket`\*, `MoveActorDeltaPacket`\*, `PlayerListPacket`, `PlayerLocationPacket`\*, `PlayerSkinPacket`, `PlayerUpdateEntityOverridesPacket`\*, `ResourcePackClientResponsePacket`, `ResourcePacksInfoPacket`, `SetActorDataPacket`, `SetScorePacket`\*, `SetScoreboardIdentityPacket`\*, `StructureBlockUpdatePacket`\*, `SubChunkPacket`\*/`SubChunkRequestPacket`\*. (\* = not implemented in Zenith at all yet — no existing packet to break, just a note for whoever scaffolds them later via `protocol-import scaffold`.) Of Zenith's actually-shipped packets, the debt set is: StartGame, LevelChunk, MovePlayer, PlayerAuthInput, CraftingData, CreativeContent, ItemStackRequest, ItemStackResponse, AddItemActor, AddPlayer, PlayerList, PlayerSkin, ResourcePackClientResponse, ResourcePacksInfo, SetActorData — **15 packets**.

**Packet naming audit (the other half of this session's ask):** cross-checked all 67 IDs in `ProtocolInfo.cs` against the `r/26_u4` schema by packet ID — **zero name mismatches**. Every Zenith enum name PascalCases to exactly the class name Mojang's schema uses for that ID. What was likely remembered as "wrong name" is probably the ADR §76 addendum's note on 3 packets with *field-level* structural drift (`UpdateBlockPacket`, `ContainerOpenPacket`, `UpdateAdventureSettingsPacket`) — those are outside this changelog's Cereal list (unaffected by 2169) and remain accurate as documented in §76.

**Not touched by this ADR:** the 3 §76 structural-drift packets (`UpdateBlockPacket`'s grouped `BlockPosition`, `ContainerOpenPacket`'s `TargetActorId`, `UpdateAdventureSettingsPacket`'s collapsed field) — those are a separate, smaller, non-Cereal migration and stay tracked under §76/roadmap, not folded into this debt list.

**Non-goals:** Actually implementing the Cereal encoding for any of the 15 debt packets in this ADR (tracked as future work, packet-by-packet); a generic "Cereal serializer" abstraction ahead of a second real user (mirrors the ADR §76/§78 stance on not building infra before a demonstrated second case); reverting to 1001 (the version number itself is correct now — the gap is in packet bodies, not the announced number).

**Status (ago 2026):** Shipped — `ServerIdentity` bump, boot warning, `StartGameWireTests` updated. Debt list tracked in `roadmap.md`; the 15 packets remain pre-Cereal shaped.

### 80. Architecture audit pass — findings and fixes

**Choice:** Full-codebase sweep against `ARCHITECTURE.md`'s freeze list, layering rules, and rule 6 (handlers queue intent, never mutate state directly), plus a test-coverage gap check against the fanout-test precedent (`BlockCrackFanoutTests.cs` et al.). Recorded here so the audit itself — and what was deliberately *not* changed — has a trail, not just the diffs.

1. **Freeze list / layering:** clean. No `Scheduler`/`ECS`/`Actor`/`ServiceLocator`/DI-container/`Network/` catch-all in `src/`. `Packets/` has no `Player`/`World`/`Server` references; `Gameplay/` has no `DataPacket`/`BinaryStream`/`ProtocolInfo` references; `Protocol/` holds no gameplay decisions. `libs/nbt`/`libs/leveldb` have zero references to `Zenith.*` gameplay code.
2. **`Player.SelectedHotbarSlot` direct write in `InGameUseItemHandler`/`InGameInventoryHandler`** — flagged as a literal rule-6 violation (handler mutating state instead of queuing intent), but on inspection this is a **known, intentional exception**, already documented in `EquipmentSystem`'s own doc comment ("Handler só escreve `SelectedHotbarSlot`; fan-out neste tick"). `int` writes are atomic on the CLR, `EquipmentSystem` diffs against `LastReplicated*` every tick and self-corrects, same dirty-flag shape as `MovementSystem`'s pose diffing (ADR §44). **Not changed** — routing this through a pending-intent queue would be indirection for a single atomic scalar with no real race, not a correctness fix. This ADR entry *is* the formal note rule 6 was missing.
3. **Stale RakNet comments:** `RakNetSession.HandleIncomingFrameSet` had `// TODO: duplicate framesets` and `// TODO: out of order` on lines that already handle both cases (sequence-dedup set + `< LastInputSequence` stale check). Real per-message ordering lives separately in `HandleFrame` via `InputOrderIndex`/`InputHighestSequenceIndex` (a proper reorder-and-buffer mechanism, verified by reading it, not assumed). The TODOs were describing already-shipped behavior — replaced with accurate comments; **no behavior changed**.
4. **Test coverage gaps closed:** `TimeSyncSystem`, `FloorDropFanout`, `BlockSoundFanout` had production logic (tick-gating math, spiral free-cell search, subject/peer exclusion) with zero test file, despite direct siblings (`MovementSystem`-adjacent tests, `BlockCrackFanoutTests`) already establishing the pattern for this exact shape of class. Added `TimeSyncSystemTests.cs`, `FloorDropFanoutTests.cs`, `BlockSoundFanoutTests.cs` using the existing `IntentTestFixture` harness — no production code changed to make these testable, the harness already supported it.
5. **`BlockSoundFanout`'s stale `"protocol 1001"` comment** — updated to note the sound-name-string mapping is unaffected by the §79 bump (not in the Cereal debt list), rather than silently left pointing at a version number that's no longer accurate.

**Non-goals:** Rewriting `SelectedHotbarSlot` into a pending-intent queue (see point 2 — deliberately not a fix); touching `robustness-dx-debt.md`/`alpha-gate.md` "Jul 2026" dates (those are point-in-time human-smoke evidence markers, not staleness — rewriting them to a later date would misrepresent when verification actually happened); resource-pack / snappy TODOs (already tracked, lower urgency, left for their own pass).

**Status (ago 2026):** Shipped — RakNet comment fix, 3 new test files (12 tests), `BlockSoundFanout` comment fix, this ADR as the formal record of the `SelectedHotbarSlot` exception.

### 81. Cereal migration methodology — no live 2169 reference exists yet

**Choice:** Before writing any Cereal-format wire code (§79 debt), verify field-by-field against the Mojang `r/26_u4` JSON schema **and** cross-check with third-party live implementations (gophertunnel, `bedrock-v/protocol`, Endstone — all locally cloned per `dx.md`). Started with the smallest suggested debt group: `AddItemActorPacket` + `AddPlayerPacket` + `SetActorDataPacket` (shared `SynchedActorData::CopyableDataList`).

**Finding 1 — no live reference is at 2169 yet.** Pulled and checked all three: gophertunnel `CurrentProtocol = 2168`, `bedrock-v/protocol`'s newest folder is `v2168` (no `v2169`), Endstone's `NetworkProtocolVersion = 2168`. All one version **behind** the Cereal changelog (`changelog_2168_07_07_26.md` documents the 2168→2169 delta — the ~23-packet Cereal list *is* that delta). This means none of them reflect the Cereal-converted shape for any packet the changelog lists — their current code shows the **legacy** shape being replaced, not the new one. Concretely: gophertunnel's `Writer.EntityMetadata` writes each entry's type **twice** (`Varuint32` then a redundant `Uint8` "legacyDataType") — inspected this before writing any code and nearly used it as ground truth, which would have been backwards: the 2169 changelog's own words for this exact packet are *"tagged variant payloads (discriminator + value) rather than the legacy hand-rolled DataItem byte stream"* — i.e. the double-write **is** the legacy behavior being removed, not a new requirement.

**Finding 2 — `SynchedActorData`/`DataItemEntry` needs no code change.** The 2169 JSON schema (`DataItemEntry.json` → `ID` uvarint32 + `Payload`: `Type` as a single `Enum-as-Value` byte + `Value`) is byte-identical to what `EntityMetadataWriter` already emits (`WriteUnsignedVarInt(key)` then `WriteUnsignedVarInt(type)` then value) — for the small enum values Zenith uses (0–8), `UnsignedVarInt` and a raw single byte produce the exact same bytes, so there's no daylight between "legacy hand-rolled" and "Cereal tagged variant" for this specific field shape. Verified this holds for `SetActorDataPacket`, `AddItemActorPacket`, and `AddPlayerPacket`'s entity-data fields (all reuse the same writer).

**Finding 3 — `AddPlayerPacket`'s `Carried Item` genuinely changed, but is the wrong field to touch blind.** The changelog explicitly calls this one out: *"The carried item is now captured via `ItemStack::getStrippedNetworkItem()`... the item-stack net id variant is no longer included and network user data is stripped."* This is a real, 2169-specific shape change (JSON schema: `Id` as raw `int16`, not the `VarInt` Zenith currently writes). But this exact field is the one §12-adendo already documents as having **crashed a real Bedrock client** once (Jul 2026, using the "correct-per-docs" `NetworkItemStackDescriptor` shape over the legacy `ItemStackWrapper` VarInt shape). With no live 2169 reference to cross-check against and no way to test against a real client in this environment, implementing this from the JSON schema alone would repeat exactly the mistake that produced that crash — possibly for a different reason this time, possibly the same one. **Not implemented.**

**Non-goals:** Changing `NetworkItemStack.WriteItemStackWrapper`'s item-id encoding (Finding 3) without either a live 2169 reference implementation or real-client validation; treating gophertunnel/`bedrock-v`/Endstone as ground truth for any packet on the §79 Cereal list until they themselves reach 2169 (they remain valid ground truth for everything **not** on that list).

**Status (ago 2026):** Verified, no code change. `AddItemActorPacket`/`SetActorDataPacket`'s entity-metadata fields confirmed 2169-compliant as-is. `AddPlayerPacket`'s entity-metadata field likewise. `AddPlayerPacket`'s `Carried Item` (and by extension `AddItemActorPacket`'s `Item`, same underlying type) remains the one open question in this group — tracked in `roadmap.md`, blocked on a live 2169 reference or human client smoke test, not on more analysis.

**Superseded in part by §82:** a fourth reference (`EndstoneMC/bedrock-protocol`) surfaced after this entry closed — it models 2168 *and* 2181, with an explicit `until=`/`since=` diff for exactly the fields this entry flagged. Read §82 alongside this one; the methodology conclusion (no live 2169-exact reference, treat gophertunnel/`bedrock-v`/Endstone's *current* code as legacy-shape evidence only) still holds, but the "open question" in Finding 3 is closed there.

### 82. Cereal item-stack shape for AddPlayer/AddItemActor — implemented

**Choice:** Close the one open item from §81 (`AddPlayerPacket.Carried Item` / `AddItemActorPacket.Item`) and fix the entity-metadata double-write §81 didn't catch, using `EndstoneMC/bedrock-protocol` (cloned into `D:\Development\bedrock\endstone-bedrock-protocol`, not yet part of the `dx.md` reference set — added there) as the deciding source. Chosen over the other four candidates checked in the same pass (`jv2w/BedrockProtocol-Cpp`, `GlacieTeam/ProtocolLib` — protocol ≤1001 only, `PrismarineJS/bedrock-protocol`, `M9CHKO/bedrock-protocol`) because it's the only one that **versions its schema explicitly** (`@type(until=2168)` / `@type(since=2168)` pairs, modelling 975/1001/2168/2181 side by side) instead of describing only its current HEAD shape — letting a diff be read directly off the source instead of inferred from a single snapshot. Its own `CLAUDE.md` documents a rigorous, decompilation-backed sourcing methodology (bedrock-headers for names, protocol-docs for wire shape, gophertunnel for golden bytes, BDS decompilation as tiebreaker) — the most corroborated reference found this session.

1. **Entity metadata really does double-write the type at 2168+ — §81's "no change" conclusion was wrong.** `DataItemEntry.payload` is a tagged union; per this schema and its compiler's `VariantFieldGenerator` (verified by reading the C++ codegen itself, not just the schema prose): a `uvarint32` discriminator is written first, *then* the chosen payload struct's own fields — and every `since=2168` payload variant (`DataItemBytePayload`, `DataItemPosPayload`, etc.) declares its own leading `type: DataItemType` field. Two writes of the same value, not one. `EntityMetadataWriter.WriteEntryType` now writes it twice; every call site (`WriteVisibleNameMetadata`, `WriteFlagsOnly`) goes through it, so `AddPlayerPacket`, `AddItemActorPacket`, and `SetActorDataPacket` all pick up the fix without their own changes.
2. **`SerializedNetworkItemStackDescriptor` (the `since=2168` variant) has no air-item early-out** — every field is always written, unlike the `id != 0`-gated legacy `NetworkItemStackDescriptor`. `NetworkItemStack.WriteSerializedNetworkItemStackDescriptor` (new method) writes: `id` (fixed `int16`, not `VarInt`), `count` (`uint16`), `aux_value` (`uvarint32`), `net_id_variant` (bool has-flag + bare signed `varint32` if present — **no tag byte**, since "the tag is gone" at 2168+ and the case is read back from sign/parity; Zenith only ever sends a non-negative server-assigned id outbound, so the omitted tag never mattered here), `block_runtime_id` (`uvarint32`, not the old `VarInt`), `user_data_buffer` (empty `bytes` = a lone `uvarint32` zero).
3. **`AddPlayerPacket.HeldItem` and `AddItemActorPacket.Item` now call the new writer**; `NetworkItemStack.WriteItemStackWrapper` (the old VarInt-id shape, no remaining callers) deleted rather than left dead. `WriteNetworkItemStackDescriptor` (InventoryContent/MobEquipment) and `WriteItemStack` (CreativeContent/CraftingData) are untouched — different packets, different call sites, not part of this ADR's scope (`WriteNetworkItemStackDescriptor` still carries the pre-2168 net-id tag byte the DSL says is now gone; that's `PlayerAuthInputPacket`'s roadmap row, not this one).
4. **This reverses §12's empirical finding** ("`NetworkItemStackDescriptor` crashed a real client, use `ItemStackWrapper` instead") **for the 2169 case specifically.** That finding was true for whatever protocol Zenith spoke when it was written (well below 2168) — the fixed-`int16` shape was premature there. Now that `ServerIdentity.ProtocolVersion` is 2169 (past the Cereal cutover per §79/§81), the fixed-`int16` `SerializedNetworkItemStackDescriptor` is what a 2169-announcing server is expected to send; continuing to send the old `VarInt` shape would now be the mismatch. §12's finding stays correct read historically — it just no longer describes the server Zenith currently announces itself as.

**Verification:** No live client available, so `CerealItemAndMetadataTests.cs` decodes the exact byte layout by hand (field-by-field, asserting `IsEndOfFile` after the last field to catch under/over-run) rather than only checking round-trip symmetry against Zenith's own (equally-could-be-wrong) `Decode`. Two pre-existing tests asserted the old shape and were updated: `ItemActorPacketTests.AddItemActor_encode_shape_uses_serialized_network_item_stack_descriptor` (renamed from `..._item_stack_wrapper`) and `InventoryRearrangeTests.AddPlayer_encode_carried_item_uses_serialized_network_item_stack_descriptor` (renamed from `..._legacy_held_item` — its old assertion, "air is shorter than a real item," is no longer true now that the shape has no early-out, so it now asserts equal length plus the full field layout instead).

**Non-goals:** `WriteNetworkItemStackDescriptor` / `WriteItemStack` (different call sites, different ADR — tracked separately in `roadmap.md`'s `PlayerAuthInputPacket`/`ItemStackRequestPacket`/`ItemStackResponsePacket` row); a live 2169-exact reference still does not exist (`endstone-bedrock-protocol` models 2168 and 2181, straddling but not naming 2169 exactly) — treated as sufficiently authoritative here because 2181 is *further* along the same Cereal direction as 2169 and the fields in question are unchanged between the two per the DSL's own `since=2168` (not `since=2181`) gating, but this is still not the "test against a real 1.26.50 client" bar `protocol-churn.md` asks for.

**Status (ago 2026):** Shipped — `EntityMetadataWriter.WriteEntryType`, `NetworkItemStack.WriteSerializedNetworkItemStackDescriptor`, `AddPlayerPacket`/`AddItemActorPacket` updated, `WriteItemStackWrapper` deleted, 5 new tests + 2 existing tests updated, 509/509 green. `endstone-bedrock-protocol` added to the local reference set (`dx.md`).

### 83. ResourcePackClientResponse status was off-by-one — found by running the smoke bot

**Choice:** Fix `ResourcePackClientResponsePacket`'s `STATUS_*` constants from 1-indexed (`REFUSED=1..COMPLETED=4`) to the real 0-indexed wire values (`REFUSED=0..COMPLETED=3`), and get `zenith-bedrock/zenith-smoke-bot` (ADR §58) actually running again — it had silently stopped being useful once Zenith bumped past what the bot's dependencies support.

1. **Updated the smoke bot for the §79 protocol bump.** No release of `bedrock-protocol` (the bot's client library) or its `minecraft-data` dependency has cataloged protocol 2169 yet — checked directly (`bedrock-protocol@3.58.0`, `minecraft-data@3.113.0`, both current as of this session): newest is 1.26.40 / protocol **2168**, same ceiling every other reference hit this session (§81). Fix: the bot now overrides `client.options.protocolVersion` to `2169` *after* `bedrock.createClient()` returns (the value is read lazily when the handshake packets are actually queued, well after construction, so the override lands before any bytes go out) while `version` stays `1.26.40` for the packet serializer/deserializer schema. This claims Zenith's real protocol on the wire — passing `ProtocolGate.Evaluate`'s exact-match check — while using the nearest packet shapes the library actually ships. Documented as a deliberate, known-limited setup in the bot's `config.ts`: it validates everything **not** on the Cereal-migration list, but a decode error on a packet that *is* on that list (`AddPlayer`, `AddItemActor`, `SetActorData` today) isn't proof of a Zenith bug — the bot's own deserializer doesn't understand the Cereal shape yet either.
2. **`smoke:join` immediately caught a real, independent bug once the handshake could complete at all**: `ResourcePackClientResponsePacket.STATUS_HAVE_ALL_PACKS`/`STATUS_COMPLETED` were 1-indexed (`3`/`4`); the real wire enum is 0-indexed (`2`/`3`) — confirmed against three independent sources (Mojang's own `ResourcePackResponse.json` enum, gophertunnel's `iota`-based consts, minecraft-data's `mapper` table). The bot's "completed" response (wire value 3) landed on Zenith's `STATUS_HAVE_ALL_PACKS` case instead of `STATUS_COMPLETED` — `ResourcePacksSessionHandler` kept replying with `SendStack` and never sent `StartGame`, so every join silently hung at the resource-pack step. This predates the 2169 bump entirely; it would have hung a real 1.26.33 client too. Not previously caught because nothing had exercised this exact state transition end-to-end before.
3. **After the fix, `smoke:join` gets past `StartGame` and fails decoding `ItemRegistryPacket`** (`array size is abnormally large, not reading: 168819959`) — not a packet on the Cereal list, so likely either a genuine Zenith wire gap in the per-entry NBT encoding or a bot-side parsing gap for that specific field, not yet root-caused. **Not fixed in this session** — recorded as a known next step, not silently dropped.

**Why the smoke bot matters here:** this off-by-one is exactly the class of bug static review and unit tests can't catch — the packet encodes fine in isolation (a lone `byte`, machine-checkable only against the *wrong* baseline if that baseline is itself wrong), and only a real client handshake sequence exercises the actual state-machine transition it breaks. `protocol-churn.md`'s "smoke: login → InGame on that client" step exists for exactly this reason.

**Non-goals:** Root-causing the `ItemRegistryPacket` decode failure (tracked as follow-up, not this ADR); getting the smoke bot to genuinely validate Cereal-shaped packets (blocked on `bedrock-protocol` itself reaching 2169 — same blocker as §81/§82).

**Status (ago 2026):** Shipped — `ResourcePackClientResponsePacket` constants fixed + `ResourcePackClientResponseStatusTests`, 510/510 green. `zenith-smoke-bot`: `bedrock-protocol` bumped to `^3.58.0`, `protocolVersion` override added to `client.ts`/`join.ts`, `config.ts` documents the deliberate version mismatch and its scope limit, `README.md` version-pin table updated.

### 84. Registry data (`data/*.{nbt,json}`) already at the community frontier — no 1.26.40+ palette exists yet

**Choice:** Checked whether any reference has a newer wire-ready `block_palette.nbt` / `item_palette.json` / `creative_items.json` than what Zenith embeds, before pulling anything in. Conclusion: **nothing to pull** — recorded so a future contributor doesn't re-spend the same research pass.

1. **`PowerNukkitX/GameData`** (`org.powernukkitx:gamedata`, pinned `r-26_u4-SNAPSHOT` in PNX's own `libs.versions.toml` — a misleading label) — diffed its `kaooot/` folder against Zenith's embedded files directly: `block_palette.nbt` is **byte-identical**, `item_palette.json` has the **identical item set** (1933 entries, same names/ids), `creative_items.json` has **identical content** (1875 items, 123 groups) modulo JSON whitespace. The repo's actual data commits stop at "update to 26.30" (Jul 2026) despite the SNAPSHOT version string implying r26_u4/2169 currency.
2. **`minecraft-data`** (npm, checked at the current `3.113.0`) — same ceiling: its `1.26.40/` folder has only `protocol.json`/`version.json` (wire schema metadata), no `items.json`/`blocks.json` content tables at all yet for that version. `1.26.30/` is the newest version with full data tables.
3. **`Mojang/bedrock-samples`** (the actual official source) — genuinely newer, tagged up to `v1.26.40.27-preview` (pushed Aug 5, 2026). But it ships the *logical* vanilla definitions (`metadata/vanilladata_modules/mojang-{blocks,items}.json` — behavior/resource-pack authoring data), not a wire-ready palette with assigned runtime IDs in game-load order. Producing `block_palette.nbt`/`item_palette.json` from it requires reproducing Mojang's runtime-ID assignment algorithm — an extraction step every downstream project (PM's `BedrockData`, PNX's `GameData`) does by hand per release, and **nobody has published that extraction for 1.26.40+ yet**. PMMP's own `BedrockData` repo is stuck at `bedrock-1.26.30` too (PMMP announced end of support — no one left to run the extraction).
4. **`recipes.json`** (also present in PNX's `GameData`) — not applicable to "update": Zenith doesn't load recipes from a data file at all. `RecipeRegistry` is two hand-coded recipes by design (curated, matches `Blocks`/`DigProfiles`/`CreativeCatalog`'s existing stance) — adopting a full vanilla recipe JSON is a scope decision (Horizon 2: "3×3 crafting table / Mojang recipe dump"), not a data refresh.

**Why this matters beyond "nothing to do today":** it's a genuine, structural gap in the whole community ecosystem right now, not a Zenith-specific lag — the wire *protocol* schema research (ADR §81/§82, `endstone-bedrock-protocol`) got ahead of the wire *data* extraction pipeline, which apparently nobody has restarted since PMMP wound down. Whoever picks up `roadmap.md`'s Cereal-migration debt eventually reaching a version where the item/block registry itself needs to move past 1.26.30 will need to either build this extraction from `bedrock-samples` directly, or wait for a community project to publish it first.

**Non-goals:** Building a `bedrock-samples` → wire-palette extraction pipeline now (no demonstrated need — Zenith's shipped registry already covers every block/item the game currently places/crafts); adopting the full vanilla `recipes.json` (Horizon 2, needs its own ADR + demonstrated need per the freeze-list discipline).

**Status (ago 2026):** Research only, no code change. Palette refresh has no action item until a newer wire-ready extraction exists somewhere, or Zenith builds its own.

### 85. `CreativeContentPacket.category` width bug — found via zenith-smoke-bot's `debug:capture`

**Choice:** Fix `CreativeContentPacket` writing group `category` as a fixed 4-byte `int32` when the wire expects a single byte (`mapper<u8>`), found by isolating the roadmap §79-priority-7 "ItemRegistry decode failure" — which turned out to be a misattribution, not actually ItemRegistry at all.

1. **The original priority-7 note was wrong about which packet fails.** `zenith-smoke-bot`'s `debug:capture` (new — dumps each inbound game packet's raw hex before `bedrock-protocol` tries to deserialize it, since each game packet is independently framed and a decode error is real evidence about *that specific packet*, not a stream-wide corruption) showed: `item_registry` (64567 bytes, 1933 entries) decodes **cleanly**. The very next packet, `creative_content` (411 bytes), is what throws `array size is abnormally large`. The two are adjacent in the send sequence and in the debug log output, which is what caused the original misattribution.
2. **Root cause:** `CreativeGroupEntry.Category` was written via `writer.WriteInt(group.Category, Little)` — 4 bytes — but the wire type is `mapper<u8>`, one byte (confirmed via `minecraft-data`'s 1.26.40 `protocol.json`: `{"category": {"type": ["mapper", {"type": "u8", ...}]}}`, no `Compression` option, so a raw byte, not a varint). With `CreativeCatalog` currently emitting 123 groups, each one over-wrote 3 extra bytes — by the time the reader reached the `items` array's own count field, it was reading garbage accumulated across ~369 misplaced bytes, producing the huge bogus count that tripped protodef's array-size sanity check.
3. **`entry_id`/`group_index` were re-checked and left alone.** Initially suspected these needed to move from unsigned `varint` to signed `zigzag32` too (protodef distinguishes the two — confirmed by reading `protodef`'s own `varint.js`: `varint` = plain `readVarInt`/`writeVarInt`, `zigzag32` = `readSignedVarInt`/`writeSignedVarInt`, genuinely different encodings). The schema types these two fields as plain `"varint"`, not `"zigzag32"` — the original unsigned `WriteUnsignedVarInt` calls were already correct. Changed them and reverted in the same session after re-reading the schema field types more carefully; recorded here so the same mistake isn't repeated.
4. **`NetworkItemStack.WriteItemStack`** (used for both group icons and item entries) was cross-checked against `ItemLegacy`'s full shape (`network_id`: zigzag32, `count`: lu16, `metadata`: varint, `block_runtime_id`: zigzag32, `extra`: varint-length-prefixed `ItemExtraDataWithoutBlockingTick` — `has_nbt`(lu16 mapper) + `can_place_on`/`can_destroy` (li32-counted arrays)) — matches field-for-field already, no change needed.

**Verification:** `debug:capture` against a live local server confirmed the fix — `creative_content` now decodes without error, `crafting_data` (the next packet) fails independently (§ tracked separately, not this ADR — see `roadmap.md`). Added `CreativeContent_group_category_is_a_single_byte` (byte-level, asserts `IsEndOfFile`) to `zenith.Tests`.

**Non-goals:** `CraftingDataPacket`'s independent, larger structural mismatch (separate ADR when tackled — needs the same multi-reference care as a Cereal-migration packet, not a quick field-width fix); auditing every other hand-written packet for the same class of bug preemptively (fix opportunistically as the smoke bot surfaces them, per this ADR and §83 — that's the point of running it).

**Status (ago 2026):** Shipped — `CreativeContentPacket.Category` write fixed to one byte, verified live against the smoke bot, `CreativeContent_group_category_is_a_single_byte` test added, 511/511 green.

### 86. `CraftingDataPacket` structural rewrite for protocol 2168+

**Choice:** Rewrite `CraftingDataPacket`/`ShapelessCraftingRecipe` to the real 2168+ wire shape (roadmap priority 8, deferred from §85 as "needs the same multi-reference care as a Cereal-migration packet"). Used `endstone-bedrock-protocol`'s versioned `until=2168`/`since=2168` schema pairs as the primary source (it's the only reference that shows *both* eras side by side, so the delta reads directly instead of being inferred), cross-checked against gophertunnel's `crafting_data.go`/`recipe.go` — gophertunnel is *at* 2168, and this restructuring happened at/before that boundary (not part of the 2168→2169 Cereal wave §81 already flagged as unsafe to trust gophertunnel for), so gophertunnel's current code is genuine, live-tested ground truth here, unlike the AddPlayer/SetActorData case in §81/§82.

1. **The packet moved from one combined recipe array (each entry self-tagged with its type) to twelve separate per-recipe-type arrays** in a fixed order: `shaped_recipes`, `shapeless_recipes`, `multi_recipes`, `user_data_shapeless_recipes`, `shapeless_chemistry_recipes`, `shaped_chemistry_recipes`, `smithing_transform_recipes`, `smithing_trim_recipes`, `potion_mix_entries`, `container_mix_entries`, `material_reducer_entries`, `clear_recipes`. Zenith only ever populates `shapeless_recipes`; every other array is a genuine empty `uvarint32(0)`, not a stand-in for something unimplemented.
2. **`RecipeIngredient` moved to a tagged-descriptor model** instead of a flat network-id+metadata pair: a `uvarint32` variant number *and* a literal string label for the same variant (`"name"` for the common case — confirmed by reading gophertunnel's `ItemDescriptorCount` writer, which writes both), then the item's real string name (not a network id) + metadata (`varint32`, signed) + the ingredient's required count (`varint32`, signed). `DefaultDescriptorInput` changed from `(short NetworkId, short Metadata, int Count)` to `(string Name, int Metadata, int Count)` — `CraftingDataBuilder` already had the item's name in scope (from `Blocks.TryGetName`) before converting it to a network id for the old shape, so no new lookup was needed.
3. **The recipe's own trailing shape** (per `ShapelessRecipePayload`/gophertunnel's `marshalShapeless`): `recipe_id`(string), `input`(array), `output`(array of `ItemLegacy` — unchanged, already matched via `NetworkItemStack.WriteItemStack`), `uuid`(16 bytes, `Guid.Empty`), `block`(string), `priority`(`varint32` signed), `unlocking_requirement`(`Optional<RecipeUnlockingRequirement>` — Zenith writes the outer has-flag `false`, meaning "always craftable," which is what the old `UnlockAlways` byte meant too), `network_id`(`uvarint32`, **unsigned** — unlike `priority` a few fields earlier).

**Verification:** `debug:capture` against a live local server — `crafting_data` now decodes cleanly; the join sequence proceeds past it to (already-documented, Cereal-list) `level_chunk`/`move_player` failures and reaches `spawn`. Added a byte-level test (`ShapelessRecipe_encode_shape_matches_protocol_2168_layout`, asserts `IsEndOfFile`) plus updated the two existing `CraftingDataPacketTests` for the new array-order and descriptor shape.

**Non-goals:** The other eleven recipe-type payloads (`ShapedRecipe`, `MultiRecipe`, `SmithingTransformRecipe`, potion/material-reducer entries, …) — no domain need yet, Zenith ships shapeless recipes only; scaffold them from the same reference when a real recipe needs one of those shapes.

**Status (ago 2026):** Shipped — `CraftingDataPacket`/`ShapelessCraftingRecipe`/`DefaultDescriptorInput`/`CraftingDataBuilder` updated, verified live, 1 new test + 2 updated, 512/512 green.

### 87. `NetworkItemStack.WriteNetworkItemStackDescriptor` had the same class of bug — fixed at the root

**Choice:** After §86 unblocked the join sequence further, `debug:capture` surfaced `InventoryContentPacket` failing to decode ("offset out of range") whenever a slot carried a non-zero item-stack network id. Traced to `NetworkItemStack.WriteNetworkItemStackDescriptor` — shared by `InventoryContentPacket` **and** `MobEquipmentPacket` — writing an extra `stack_id_variant` tag `uvarint32(0)` between the `has_stack_id` bool and the `stack_id` value itself. The real `ItemV4` shape (confirmed against `minecraft-data`'s 1.26.40 schema) has no such tag: `has_stack_id`(bool) then a bare `stack_id`(`zigzag32`) if true, directly.

1. **Fixed once, at the shared method** rather than patching each call site — exactly the "fix at the root" the roadmap's suggested grouping for `PlayerAuthInputPacket`/`ItemStackRequestPacket`/`ItemStackResponsePacket` already anticipated (those three share this same item-stack-net-id-variant concern per that row's note); this fix now also covers `InventoryContentPacket`/`MobEquipmentPacket`, which weren't in that original grouping but turned out to share the exact same bug through the same method.
2. **Not exercised by the smoke bot until now** because it only manifests when a slot actually carries a non-zero stack net id (ISR-tracked item identity) — an air/empty-net-id inventory happens to skip the buggy branch entirely, which is likely why this went uncaught through §83's earlier `first10` runs (that never got far enough to send a populated inventory) and through unit tests (none exercised the `hasNetId == true` branch end-to-end against a real decoder).

**Verification:** `debug:capture` against a live local server — `inventory_content` now decodes cleanly even with populated, ISR-tracked slots. Added `NetworkItemStackDescriptorTests.cs` (both branches, byte-level, `IsEndOfFile`-asserted).

**Non-goals:** `WriteItemStack`/`WriteSerializedNetworkItemStackDescriptor` (already verified correct in §82/§85); `PlayerAuthInputPacket`/`ItemStackRequestPacket`/`ItemStackResponsePacket`'s own migration (still open — but now share a verified-correct helper once they're tackled, one less thing to get wrong there).

**Status (ago 2026):** Shipped — `WriteNetworkItemStackDescriptor` tag byte removed, verified live, 2 new tests, 514/514 green.

### 88. `LevelChunkPacket` / `MovePlayerPacket` Cereal migration — unblocks `smoke:join` reaching real `spawn`

**Choice:** Migrate the two Cereal-list packets sitting directly in the join critical path. Server-side bug, not a bot bug: Zenith announces protocol 2169 (Cereal-era) but was still encoding both packets pre-Cereal-shaped, so a real/bot client parsing per the announced protocol fails to decode them and never reaches `spawn`. Confirmed with a live local run + `zenith-smoke-bot`'s `debug:capture`: `LevelChunkPacket` (id 0x3a) failed decode on all 25 PreSpawn columns, `MovePlayerPacket` (id 0x13) threw `Unexpected buffer end while reading VarLong` — exactly the two packets the roadmap's debt table already named as blocking. Pulled `protocol-import pull --source mojang --ref r/26_u4` (229 packets cached) and cross-checked its JSON `required`/`x-ordinal-index` fields against `endstone-bedrock-protocol`'s `@type(since=2168)` pair for both packets — the two sources agree field-for-field, so this is root-caused, not a workaround.

1. **`LevelChunkPacket`:** `ClientRequestSubChunkLimit` (a pre-Cereal sentinel-gated field, `sub_chunks_count == 4294967294`) became a genuine Cereal optional (`varint32 | None` — presence bool then payload) since 2168; Zenith never uses request-mode, so it now always writes presence `false`. `CacheMetadata` moved from `when=cache_enabled` (i.e. absent when caching is off) to **always present** as a list — Zenith doesn't support the client blob cache, so it writes `cache_enabled=false` then an explicit empty-list count (`uvarint32(0)`). Field order otherwise unchanged (`ChunkPos → DimensionId → SubChunksCount → [new optional] → CacheEnabled → [new list] → SerializedChunkData`).
2. **`MovePlayerPacket`:** `TeleportData` (`cause` + `source_entity_type`) moved from an implicit `when=(reset_position==TELEPORT)` gate (no presence marker, the reader recomputes it from `Mode`) to an explicit Cereal optional — a presence bool is now always on the wire before the two fields, regardless of mode. Zenith already only populates the two fields when `Mode == ModeTeleport`; the fix adds the presence bool Zenith was omitting entirely.

**Verification:** Live local server + `zenith-smoke-bot`: `smoke:join` now reaches `event: spawn` (previously timed out after `start_game`); `smoke:place` passes immediately after. `dotnet test zenith.sln -c Release`: updated `MovePlayerPacketTests` (asserts the new presence bool) + new `LevelChunkPacketTests` (byte-level, asserts the optional-presence bool and the always-present empty `CacheMetadata` count), 516/516 relevant packet suite green (one unrelated pre-existing parallel-run flake in `BreakDurationTests`, tracked separately, not touched by this ADR).

**Non-goals:** `PlayerAuthInputPacket` — `smoke:break` still fails (times out waiting for the break's `update_block`) because that packet is also Cereal-list and still unmigrated; the server silently drops/misreads the bot's dig `blockActions` (quiet-ACK policy, no decode WARNING logged). Larger, higher-risk packet (high tick-rate, shares the item-stack-net-id-variant concern with `ItemStackRequestPacket`/`ItemStackResponsePacket` per the roadmap's suggested grouping) — left for its own ADR, not folded into this one. `StartGamePacket`, `PlayerListPacket`, `PlayerSkinPacket`, `ResourcePacksInfoPacket` — untouched, still pre-Cereal per the roadmap debt table.

**Status (ago 2026):** Shipped — `LevelChunkPacket`/`MovePlayerPacket` updated, verified live via smoke bot reaching `spawn`, 2 packet-wire tests (1 new, 1 updated), full suite green modulo the tracked unrelated flake.

### 89. `PlayerAuthInputPacket` Cereal rewrite — unblocks `smoke:break` (input-flags container change, not just added optionals)

**Choice:** Migrate the last Cereal-list packet on the join/dig critical path. §88 fixed `LevelChunkPacket`/`MovePlayerPacket`; `smoke:break` kept failing afterward (silent timeout, no decode WARNING — quiet-ACK policy) because `PlayerAuthInputPacket`'s dig `block_action` list was still being misparsed. `protocol-import`'s cached schemas (its `pull`/`scaffold`/`diff` commands only serve Tier A codegen packets, ADR §76) don't cover this packet — it's Tier B, hand-written by design — and neither the cached Mojang JSON nor `endstone-bedrock-protocol`'s Python schema documents one load-bearing wire detail precisely enough to trust blind: a "double presence" pattern where several optional fields carry both a decorative `_presence` bool field **and** a separate real presence bool consumed by the `option` wrapper itself. Root-caused by instrumenting the server to dump raw AuthInput bytes (`ZENITH_DEBUG_AUTHINPUT` temp env hook, removed after), then decoding them byte-for-byte in Python against `zenith-smoke-bot`'s own `bedrock-protocol` dependency's bundled `minecraft-data` `protocol.json` for its exact wire version (1.26.40) — confirmed exact, zero-leftover-byte agreement (verified `input_mode`/`play_mode`/`tick` values matched what the bot actually sent, not garbage).

1. **`input_data` changed container entirely**, not just added an optional layer: the old packed 7-bit-per-byte bitset-with-continuation-bit became a Cereal `option<array<uvarint32 count, zigzag32 flag ordinal>>` — a genuinely different encoding (list of *set* flag ordinals) rather than a bit-tested blob. Flag *numbering* itself (Sneaking=8, StartSprinting=25, VerticalCollision=50, …) is unchanged across the boundary, so existing constants stayed valid; only the decode loop changed.
2. **`transaction` / `item_stack_request` / `block_action` / `vehicle_rotation` / `predicted_vehicle` stopped being implicitly gated by `input_data` bit tests** (the old `when=input_data.test(...)` pattern) and became independent Cereal optionals with their own presence bool — decoupled from whatever the flag list says. Each carries the "double presence" pattern above: a `..._presence` bool field that real captures show is **decorative** (client writes it inconsistently with actual presence) followed by the `option`'s own bool, which is what actually gates the payload. Zenith's decoder reads and discards the decorative bool, then branches on the real one.
3. **`PlayerBlockActionData` simplified**: since 2168 every entry unconditionally carries `action, pos.x, pos.y, pos.z, face` — no more per-action-type gating on whether a position is meaningful (the old code only read pos/face for the 5 destroy-related action types).
4. **The embedded UseItem transaction** (`AuthItemInteraction`) was rewritten against the real `TransactionUseItem`/`ItemV4` shapes from the same `protocol.json`: `face` is a raw `u8` (not a varint like the surrounding fields), `held_item` uses the fixed-i16-network-id ItemV4 shape already verified correct in §82/§87 (`SkipNetworkItemStackDescriptor`, field-for-field matching `NetworkItemStack.WriteNetworkItemStackDescriptor`), and the trailing `client_prediction`/`client_cooldown_state` are single mapper bytes.
5. **Trailing `analogue_move_vector`/`camera_orientation`/`raw_move_vector`** are unconditionally present since 2168 (previously read defensively as "may be truncated" — that defensive `try/catch` is gone, replaced by a straight read now that the preceding optionals are correctly bounded).

**Non-goals:** the embedded `item_stack_request`'s *internal* shape — `PlayerAuthInputPacket.Decode` still delegates to `ItemStackRequestPacket.SkipEmbeddedRequest`, whose action-type reader is confirmed still pre-Cereal (`ReadByte` for a field that's actually a compressed varint, missing a `legacy_type_id` byte) — not exercised by current smoke coverage (`has_item_stack` was always `false` in every capture used to verify this ADR), left as the already-tracked `ItemStackRequestPacket`/`ItemStackResponsePacket` roadmap row. `StartGamePacket`, `PlayerListPacket`, `PlayerSkinPacket`, `ResourcePacksInfoPacket`, `ResourcePackClientResponsePacket` — untouched, still pre-Cereal per the roadmap debt table.

**Verification:** Live local server + `zenith-smoke-bot`: `smoke:break` now passes (`OK: break → air`); re-ran `smoke:first10` and `join`/`place`/`break`/`inv-hotbar` all pass in sequence (next failure, `smoke:respawn`, is an unrelated void-detection/death-pipeline issue, not a wire bug — flagged separately, not part of this ADR). `dotnet test zenith.sln -c Release`: rewrote `PlayerAuthInputDecodeTests` (byte-level, `IsEndOfFile`-asserted, covers empty-flags, input-data-list flags, block-action-only, and use-item-transaction cases) and dropped the now-obsolete bitset-helper test in `EntityMetadataTests`; 515/515 green (same tracked unrelated `BreakDurationTests` flake as §88, not touched here).

**Status (ago 2026):** Shipped — `PlayerAuthInputPacket` rewritten, verified live via `smoke:break`/`smoke:first10`, 1 test file rewritten + 1 obsolete test removed, full suite green modulo the tracked unrelated flake.

### 90. `ItemStackRequestPacket` / `ItemStackResponsePacket` — action-number and optional-field bugs, not just Cereal debt

**Choice:** Close the last row of the §89 "do together" grouping. Cross-checked both packets against `zenith-smoke-bot`'s own `bedrock-protocol` dependency's bundled `minecraft-data` `protocol.json` (1.26.40) — the same source that byte-verified §88/§89 — and found the actual bugs were **not** the Cereal double-optional pattern this grouping was opened to investigate, but two independent, more basic correctness bugs plus one genuine optional-field miss on the response side:

1. **`ItemStackRequestPacket`'s action-type numbering was simply wrong**, unrelated to any protocol-version boundary: Zenith's constants included `ActionPlaceInContainer = 7` and `ActionTakeOutContainer = 8`, two action kinds that **do not exist** in the real `ItemStackRequestActionType` mapper (`take=0, place=1, swap=2, drop=3, destroy=4, consume=5, create=6, lab_table_combine=7, beacon_payment=8, mine_block=9, craft_recipe=10, craft_recipe_auto=11, craft_creative=12, optional=13, craft_grindstone_request=14, craft_loom_request=15, non_implemented=16, results_deprecated=17`). Every action from `lab_table_combine` onward was consequently read as the wrong case — e.g. a real `lab_table_combine` (7) action byte was decoded as `ActionPlaceInContainer`, triggering a `count + 2×StackRequestSlotInfo` read that doesn't exist on the wire for that action, corrupting every action after it in the same request. Renumbered to match; the two invented actions are gone.
2. **Every action entry carries a `legacy_type_id: u8` immediately after `type_id`** that Zenith never read — a permanent one-byte misalignment on every single action, present since the packet was first written (not a 2168 regression).
3. **`StackRequestSlotInfo.stack_id` is a fixed `li32` (4 bytes), not a varint** — Zenith read it with `ReadVarInt()`, silently correct only when the id happened to fit and zigzag-decode to something that consumed the same byte count by coincidence (usually did not).
4. **Three other field-width mismatches found while re-deriving every action's shape from the same schema:** `mine_block`'s `network_id` is `li32` (was read as a varint), `craft_grindstone_request`'s `recipe_network_id` is `li32` (was read as a varint), and `craft_recipe_auto` had one extra phantom `u8` read before its ingredient count that doesn't exist on the wire at all (source of the byte drift for that action specifically).
5. **`craft_recipe_auto`'s ingredient skip (`RecipeIngredient2`) and `results_deprecated`'s entry skip (`ItemStackRequestInstanceDescriptor`) were both modeled on the wrong descriptor shape** (`ItemDescriptorType`'s 6-case CraftingData-era variant) — rewrote each against its own real schema (4-case mapper + `legacy_type` byte + type-specific fields + a differently-typed trailing count per descriptor).
6. **`ItemStackResponsePacket` was missing a real optional entirely, not just a Cereal-era addition:** `containers` is an unconditional `option<array<...>>` in the struct (present regardless of `status`) preceded by a decorative `containers_presence` bool — Zenith wrote a bare count only on `StatusOk` and nothing at all on error, omitting both presence markers always. Same pattern for each slot's `item_stack_id`, which the real schema also gates behind a decorative presence bool + the option's own bool — Zenith wrote a bare unconditional varint.

**Non-goals:** `ItemStackRequestPacket.ReadAction`'s remaining unsupported cases (`lab_table_combine`, `craft_recipe_auto`, `craft_recipe_optional`, `craft_grindstone_request`) stay wire-correct-but-domain-unsupported — Zenith doesn't implement grindstone/loom/auto-craft server logic, only decodes them accurately enough not to desync the stream. `PlayerListPacket`, `StartGamePacket` — untouched this ADR, see §91.

**Verification:** `dotnet test zenith.sln -c Release`: fixed 5 existing tests that encoded the old (wrong) action numbers/widths, added `ItemStackResponsePacketTests` (byte-level, `IsEndOfFile`-asserted, covers error/with-stack-id/without-stack-id), 518/518 green. Not yet re-verified against a live client — `smoke:break`/`smoke:place` don't exercise `ItemStackRequestPacket` (they use `inventory_transaction`, a different packet), so this fix is validated by wire-shape cross-reference and unit tests only; real-client validation (crafting, shift-click, container drag) is the next confirmation step, planned separately (not smoke-bot — real Bedrock client).

**Status (ago 2026):** Shipped — `ItemStackRequestPacket`/`ItemStackResponsePacket` rewritten, 5 tests updated + 3 new tests, 518/518 green. Live validation pending (real client, not smoke-bot).

### 91. `PlayerSkinPacket` / `ResourcePackClientResponsePacket` / `ResourcePacksInfoPacket` — closing the Cereal debt table, minus `PlayerListPacket`/`StartGamePacket`

**Choice:** Continue closing the roadmap's Cereal migration debt table. Same method as §88–§90: cross-check against `minecraft-data`'s live `protocol.json`, corroborate with `endstone-bedrock-protocol`'s versioned Python schema when the field in question isn't purely mechanical.

1. **`PlayerSkinPacket` had a phantom top-level trailing bool** (`IsVerified` read/written as the packet's last field) **that doesn't exist on the wire**, and was missing two real fields that live *inside* the `skin` sub-structure instead: a name-coded `trusted_skin_flag` enum (`Unset`/`False`/`True`, spelled as its member name on the wire, not a raw bool) and a `profile_hash` string, both positioned right after the skin's `overrides_player_appearance` and before the packet's own `skin_name`/`old_skin_name`. Confirmed via `endstone-bedrock-protocol`'s two `SerializedSkinRef` declarations — the hand-written one (`@type(cereal=False)`, used nowhere in Zenith) explicitly lacks these two fields with a docstring explaining PlayerList writes trust as "a trailing run of its own" instead; the default (Cereal) one, which is what `PlayerSkinPacket` references, has them. Fixed **locally in `PlayerSkinPacket`** rather than in the shared `SerializedSkin.Write`/`Read` (also used by `PlayerListPacket`, which needs a different, not-yet-touched trust encoding — see non-goals) so this fix doesn't silently break the packet it wasn't scoped to touch.
2. **`ResourcePackClientResponsePacket` was missing `response_status_name` (a string, always present regardless of status) and the conditional `resourcepackids` array** (present only when `response_status == send_packs`). Converted from a `[GamePacket]`-codegen Tier A packet to hand-written — the generator has no vocabulary for a status-conditional array, and forcing it through `[Wire]` attributes would've meant either dropping the array entirely (silent data loss) or a generator feature not worth building for one packet.
3. **`ResourcePacksInfoPacket`'s trailing `texture_packs` field was encoded as a fixed `int16(0)`** instead of a varint-prefixed array (`WriteUnsignedVarInt(0)` for the empty case) — one wrong byte width, not a missing field; `world_template` being a nested `{uuid, version}` container instead of two flat top-level fields makes no wire difference (a container with no discriminator costs zero extra bytes), so that part didn't need changing.

**Non-goals — deliberately not touched, and why each is riskier than a rushed fix:**
- **`PlayerListPacket`**: `minecraft-data`'s `PlayerRecords` schema — the same source that correctly byte-verified §88/§89/§90 — is **missing fields for this packet specifically** (no skin, build-platform, teacher/host/sub-client, or color in its `add` case), contradicted by `endstone-bedrock-protocol`'s `PlayerListPacket.AddEntry` which has all of them. This means the trusted single-source method this whole debt-closing effort has relied on doesn't hold for this packet — it needs the harder cross-reference work `endstone-bedrock-protocol` alone can give (per-entry tagged `RemoveEntry | AddEntry` union with a `uvarint32` case index, each payload *also* carrying its own redundant `action` field, plus wherever `SerializedSkinRef`'s trust fields land in this per-entry context). Attempting this under time pressure risks exactly the class of bug §90 found in `ItemStackRequestPacket` (silently wrong field/byte counts that corrupt everything downstream in the same packet). Left for a dedicated pass.
- **`StartGamePacket`**: largest single packet in the server (already flagged in the roadmap as "do last or first, not mid-stream") — same reasoning, wrong under time pressure, not attempted this session.

**Verification:** `dotnet test zenith.sln -c Release`: `PlayerSkinPacket`'s existing roundtrip test exercises the fix end-to-end unchanged (no manual byte assumptions to update); added `ResourcePacksInfoPacketTests` (byte-level, `IsEndOfFile`-asserted) and 2 `ResourcePackClientResponsePacket` decode tests (status-name-only, send_packs with pack ids); 521/521 green. Live-verified: `zenith-smoke-bot`'s `first10` still reaches `join`→`place`→`break`→`inv-hotbar` (the resource-pack handshake — `ResourcePacksInfoPacket` out, `ResourcePackClientResponsePacket` in — is on every join's critical path, so this is a real regression check, not just unit coverage). `PlayerSkinPacket`'s fix has **no live coverage** — the smoke bot doesn't exercise skin change/broadcast — real-client validation is the next confirmation step for this one specifically.

**Status (ago 2026):** Shipped — 3 packets fixed, 1 converted off codegen, 1 test file updated + 2 new test files, 521/521 green, live-reverified via `smoke:first10` for the join-path packets. `PlayerListPacket`/`StartGamePacket` remain the only Cereal debt.

### 92. `PlayerListPacket` rewritten for 2168+ — and a "double write" that isn't a repeat

**Choice:** Rewrite `PlayerListPacket` to the real 2168+ wire shape, using `endstone-bedrock-protocol`'s `player_list.py` as primary source (§89's flag that `minecraft-data`'s schema is missing fields for this packet specifically — skin, build platform, teacher/host/color — held up; cross-checked its full field list against `endstone-bedrock-protocol` and confirmed the gap independently while implementing this).

1. **No more packet-level `Type` byte + three parallel per-entry arrays** (uuid list, then add-only-fields list, then a trailing trusted-skin bool list). Each entry is now a self-contained tagged union (`RemoveEntry | AddEntry`, `uvarint32` union index in declaration order + the payload's own leading `action` member — a "double write," same mechanical shape as §82's entity metadata). Zenith never mixes Add/Remove in one packet (every call site sends exactly one entry — `EntityProtocol.SendPlayerListAdd`/`SendPlayerListRemove`), so the packet-level `Type` field stays as the domain API; only the wire encoding changed.
2. **The old trailing "trusted skins" bool array is gone** — the flag now lives inside the skin structure itself (`trusted_skin_flag` + `profile_hash`), the same two fields §91 already found and fixed locally for `PlayerSkinPacket`. Applied the identical local fix here (not the shared `SerializedSkin` type), appended once after whichever of the three `WriteSkin` branches (full skin / RGBA fallback / white placeholder) runs, so all three stay in sync automatically.
3. **The "double write" here is genuinely two different values, not a repeat — caught live, not by reading.** Read closely, gophertunnel's `playerListAction` computes the union-index variant *separately* from the payload's own action byte: `variant = 1` only when the domain action is Add, `0` otherwise, while the second field keeps the **unmodified** domain value. Since Zenith's own `TypeAdd = 0` / `TypeRemove = 1`, the wire union index is the **opposite** of Zenith's domain constants (`Remove→0`, `Add→1`) while the second field matches them directly (`Add→0`, `Remove→1`). First implementation wrote `Type` for both fields (assuming a same-value double-write, matching §82's actual pattern) — built, unit-tested, and passed, because the unit tests were written from the same wrong assumption. Only caught by decoding a real join against the live smoke bot: an intended `Add` decoded as `"remove"`. Fixed by computing the union index separately (`Type == TypeAdd ? 1 : 0`) and leaving the second field as `Type` unchanged; unit tests corrected to match.
4. **`Color`** stays a little-endian fixed `uint32` write, unverified against gophertunnel's actual encoding (`A | R<<8 | G<<16 | B<<24` packed then written **big-endian** — net byte order `[B,G,R,A]`) because Zenith only ever sends `0xffffffff` (white), which is byte-identical in any order. Documented as an open, low-priority question in the code rather than silently assumed correct.

**Why this one is the strongest argument yet for live verification over schema-reading alone:** every other fix this session (§85–§91) was schema/cross-reference work that held up once tested. This one specifically didn't — the "double write" pattern from §82 primed an assumption (same value twice) that turned out not to generalize, and no amount of re-reading the DSL text would have caught it as fast as one real decoded packet did.

**Non-goals:** Verifying `Color`'s exact byte order (no non-white value in use, no way to test); `StartGamePacket` (still deliberately not attempted — same "largest packet, needs a dedicated session" reasoning as §91).

**Status (ago 2026):** Shipped — `PlayerListPacket` rewritten, wire-variant inversion bug found and fixed live (not just schema-read), 3 new/updated tests (`PlayerListPacketTests` ×2 new, `ClientProfileParserTests`/`ClientSkinParserTests` corrected twice — once for the shape, once for the variant value), 523/523 green. Live-verified: a real decoded `player_list` packet now shows `type: "add"` for an intended Add (was `"remove"` before the fix). `StartGamePacket` remains the only Cereal debt.

### 93. `StartGamePacket` — the last Cereal debt item, and it was mostly already right

**Choice:** Cross-reference `StartGamePacket` field-by-field against `endstone-bedrock-protocol`'s `since=2168` `StartGamePacket`/`LevelSettings` and gophertunnel's current (protocol 2168) `Marshal` — the two agree byte-for-byte on every field this session checked, which is the strongest cross-validation available (gophertunnel is trustworthy ground truth exactly at 2168, per the established rule). Result: the packet's overall shape (list/optional/enum encodings inside `LevelSettings`, `GameRulesChangedPacketData`, `Experiments`, `EduSharedUriResource`, `ServerConfigurationJoinInfo`, `ServerTelemetryData`) was **already correct** — most of this packet was written against the right shape from the start, unlike the other 22 Cereal-debt packets closed this session. Three real, narrow bugs found instead:

1. **`IsLoggingChat` field that no longer exists.** endstone's `until=2168` `StartGamePacket` carries `is_logging_chat: bool = field(since=1001)` right before `server_configuration_join_info`; the `since=2168` redeclaration drops it entirely — confirmed independently by gophertunnel's current `Marshal`, which writes `ServerAuthoritativeSound` then goes straight to `ServerJoinInformation` with nothing between. Zenith was still writing this now-phantom bool. Removed.
2. **`PlayerPermissions` written as a zigzag varint instead of a raw byte.** `PlayerPermissionLevel`'s bedrock-headers underlying type is `int8`, and a one-byte enum reaches the wire as one raw byte regardless of cereal's compression trait (the DSL's own rule) — matching gophertunnel's `io.Uint8(&pk.PlayerPermissions)`. Zenith wrote `WriteVarInt(2)` (OPERATOR), which zigzag-encodes to `0x04`, not the wire's `0x02`. Single-byte-either-way meant no stream desync (everything after it stayed aligned), so this shipped silently — the client would have received an out-of-range permission level. Fixed to `WriteByte`.
3. **`EducationEditionOffer` written signed instead of unsigned.** Its enum underlies `uint32`, so cereal's default (no explicit `field(type=)` override on the `since=2168` declaration) is unsigned `uvarint32` — the pre-2168 form had an explicit signed override that no longer applies. Zenith's value is always `0`, byte-identical either way, so purely a correctness fix for future non-zero use, not an observed bug. Fixed to `WriteUnsignedVarInt`.

Also removed the boot-time `PROTOCOL WARNING` in `ZenithServer.cs` that announced `StartGame`/`PlayerList` as still pre-Cereal — both are closed now (§92, §93), and the warning was actively wrong the moment §92 shipped.

**Why so few bugs compared to the other 22 packets:** this packet's original author had evidently already modelled most of it against the post-2168 shape (or the shape simply didn't move much for the fields that matter here) — `LevelSettings`' empty-list/empty-struct fields byte-encode identically whether cereal-tagged or not when the payload is empty, which is exactly Zenith's case (no game rules, no experiments, no blocks sent). The three bugs found were all in fields with a genuine non-zero value or an actual field-presence change, which is where a schema mismatch has to show up.

**Non-goals:** Auditing every possible non-default value path (e.g. non-empty `GameRules`/`Experiments`/`Blocks` lists, a real `WorldTemplateId`, a populated `ServerJoinInformation`) — Zenith never sends any of these today, so their encoding remains unverified against a live client; flagged for whoever adds the first real use.

**Verification:** `dotnet test zenith.sln -c Release`: 3 existing `StartGameWireTests` updated for the removed field, 1 new byte-level test (`StartGame_player_permissions_is_a_raw_byte_not_a_varint`) proving `0x02` reaches the wire, not `0x04`; 526/526 green. Live-verified: `zenith-smoke-bot`'s `join.ts` reaches `event: spawn` cleanly against the rebuilt server (no decode error, no hang) — the strongest signal available since the bot's own deserializer is not fully post-2168 for every field, but a clean `start_game` → `spawn` transition on a real client-side parser is real evidence the shape holds together.

**Status (ago 2026):** Shipped — last item on the Cereal migration debt table closed. All 23 originally-flagged packets are now on the post-2168 wire shape.

### 94. `smoke:respawn` root cause — NACK resend corrupted RakNet ordering, not a wire bug

**Choice:** Root-cause the `smoke:respawn` timeout the roadmap had flagged (Yes-next priority 2) as "root-caused, not a wire/Cereal bug — looks like a RakNet transport-layer delivery issue." Confirmed and fixed the actual defect; it was neither Cereal nor a transport-layer *delivery* issue in the sense of packet loss — it was Zenith's own retransmission code corrupting reliability metadata on every resend.

1. **Confirmed server-side send is correct first.** Temporary logging showed `MovementSystem.BeginVoidDeath` firing, `Player.BeginDeath` returning `true`, and `DeathInfoPacket`/`RespawnPacket` being handed to `NetworkSession.SendDataPacket` with no exception — ruling out a domain/logic bug before touching transport code.
2. **Tried channel separation first, based on the roadmap's own hypothesis** — gave `ChunkStreamSystem`'s post-spawn per-column `LevelChunkPacket` burst (`WorldProtocol.SendLevelChunk`) its own RakNet order channel (1), leaving the join sequence (`StartGame` + PreSpawn's batch `PublishChunks`) and all other gameplay packets on channel 0 (`NetworkSession.DefaultOrderChannel`/`WorldStreamOrderChannel`). Zenith's own `RakNetSession` already tracks up to 32 independent ordered channels (`Frame.MAX_ORDER_CHANNELS`) — this machinery existed and was simply never used, every send hardcoded channel 0. **Did not fix it** — kept as a real, independently-useful improvement (bulk chunk streaming genuinely has no ordering dependency on gameplay packets), but the respawn timeout persisted.
3. **Found the actual bug via a raw low-level capture** (debugCapture-style wrapper around `bedrock-protocol`'s `readPacket`, one-off script, not committed): after a void-fall trigger, *zero* packets of any type other than `LevelChunkPacket` ever reached the client's frame layer — not a decode failure (nothing to decode, the frame never arrived), and not a logic gap (server-side send confirmed in step 1). This pointed at RakNet's own retransmission path.
4. **Root cause: `RakNetSession.HandleNack` resent frames through `SendFrameLocked`**, the same function used for a brand-new logical send. `SendFrameLocked` unconditionally re-derives `OrderIndex`/`SequenceIndex` (for Ordered/Sequenced reliability) and always assigns a fresh `MessageIndex` — correct for a genuinely new frame, wrong for a retransmission of one already sent. `OutputBackup` already stores the fully-formed `Frame` objects (post first-send metadata assignment) keyed by outer datagram sequence, precisely so a NACK resend can replay them verbatim — but `HandleNack` ignored that and ran them through the assignment logic a second time. Net effect: any dropped-and-NACKed Reliable Ordered frame got a **new, later** `OrderIndex` on resend, permanently orphaning its original slot — the receiving `RakNetSession`'s per-channel ordering buffer (`InputOrderIndex`/`InputOrderingQueue`, itself correct and already tested) then waits forever for an order index that will never arrive again, blocking every later frame on that channel behind it. This is a genuine, longstanding defect independent of chunk-stream volume — any reliable-ordered frame lost to real network loss would have hit it; the chunk burst just made a drop likely enough during this specific smoke to surface it.
5. **Fix:** `HandleNack` now calls `QueueFrameLocked` directly — re-batches the already-fully-formed frame into a new outgoing `FrameSet` (a resend legitimately needs a new *outer* datagram sequence number) without re-deriving any of `OrderIndex`/`SequenceIndex`/`MessageIndex`/`SplitInfo`.

**Why the channel-separation attempt is still worth keeping even though it wasn't the fix:** it's real, independently-justified engineering (bulk world streaming and gameplay packets have no ordering dependency on each other) and costs nothing now that it's shipped — reverting it to isolate the "real" fix would have re-introduced a needless head-of-line-blocking risk for a different scenario (a dropped chunk frame no longer has any way to block a `DeathInfoPacket`, on top of the NACK fix meaning drops recover cleanly either way).

**Non-goals:** WAL/ordering-buffer changes on the receive side (`InputOrderIndex`/`InputOrderingQueue` were already correct — confirmed by reading them, not assumed); auditing every other `Priority.Immediate` call site for a similar assumption (this was the only place a `Frame` object gets replayed rather than freshly constructed); `smoke:double-chest`'s unrelated flake (place-cell collision from repeated smoke runs reusing overlapping coordinates — pre-existing, not touched by this ADR).

**Verification:** New `raknet.Tests/NackResendTests.cs` (2 tests): a NACK-triggered resend preserves the original frame's `OrderIndex`/`SequenceIndex`/`MessageIndex` exactly, and a genuinely new frame sent afterward still gets the correct next `OrderIndex` (not one inflated by the resend). 38/38 `raknet.Tests` green, 636/636 full suite green. Live-verified: `smoke:respawn` now passes (`event: death + respawn searching 0` → `OK: void death → respawn`); reran the full `smoke:first10` chain — `join`/`place`/`break`/`inv-hotbar`/`respawn`/`chest-open` all pass in sequence (the unrelated `double-chest` flake noted above is the only failure, pre-existing).

**Status (ago 2026):** Shipped — RakNet order-channel split (`NetworkSession.WorldStreamOrderChannel`) + the actual fix (`HandleNack` uses `QueueFrameLocked`), 2 new regression tests, live-verified via `smoke:respawn` and `smoke:first10`. Closes roadmap Yes-next priority 2's remaining blocker.

### 95. Real falling_block entity — replaces §57's instant column-teleport

**Choice:** Give sand/gravel a real, client-visible falling entity instead of §57's cell-tick "compute final landing Y and teleport there in one tick" — a deliberate simplification at the time (`GravityPendingStore`'s own doc comment already named the tradeoff: "Dragonfly uses falling_block entities … Zenith cell-tick keeps this low"). Cross-checked the wire shape against Dragonfly (`FallingBlockBehaviour` — real per-tick gravity/drag physics, `minecraft:falling_block` entity, `Solidifiable`/`damager` interfaces for concrete/anvil) and PowerNukkitX's `EntityFallingBlock` (confirms the client renders the correct block texture via **DATA_VARIANT** entity metadata set to the block's runtime/state id — `setDataProperty(ActorDataTypes.VARIANT, blockState.blockStateHash())`).

1. **New `AddActorPacket`** (0x0d) — Zenith had no generic non-player/non-item actor spawn packet at all (only `AddPlayerPacket`/`AddItemActorPacket` existed). Field order cross-checked byte-for-byte against gophertunnel's current (protocol 2168) `Marshal` and endstone-bedrock-protocol's since=2168 shape — both agree exactly, so this is trustworthy ground truth, not a Cereal-debt guess.
2. **`EntityMetadataWriter.WriteVariantMetadata`** — FLAGS (gravity + collision only, no name) + DATA_VARIANT (key 2, confirmed against `ActorDataIDs.VARIANT` and PowerNukkitX's usage above) = block runtime id.
3. **New non-player move helper** (`EntityProtocol.SendMoveActorAbsoluteRaw`) — the existing `SendMoveAbsolute`/`SendMoveAbsolutes` unconditionally add `EntityHitboxes.AbsoluteWireY`'s +1.621 player eye-height offset; reusing them for a falling block would have floated it 1.6 blocks above where it visually should be. Falling blocks (and any future non-player actor) need the raw position.
4. **`FallingBlockStore`** (World layer, RAM-only, SoftCap 512 as a safety net — the real throttle stays `GravityPendingStore.MaxStepsPerTick` capping *new* falls started per tick) holds in-flight entries: block runtime id, column (X/Z), and a live `Y`/`VelocityY`/`LandY`.
5. **Landing is recomputed live every tick from current world state, not decided once at spawn.** First attempt precomputed a single landing Y at spawn time (mirroring §57's old scan) and just animated toward it — this broke on a multi-block cascade: two entities racing for the same column both compute the same stale target before either has actually landed, so the second one either overwrites the first or vanishes. Found via a **unit test failure** (`Dig_under_tower_cascades_over_ticks`, landed block was air instead of sand), not by inspection. Fixed by having each active entry check, every tick, whether its *current* cell is already occupied (someone else landed there first → rest one cell above instead of overwriting) and whether the cell below is now solid (land) or still air (keep falling) — the same self-correcting recheck real falling-block entities do, which is what makes a multi-block tower naturally restack without explicit inter-entity coordination.
6. **Physics constants** (`Gravity = 0.04f`/tick, drag retention `0.98f`) match PowerNukkitX's `EntityFallingBlock` (`getDefaultGravity()`/`getDrag()`) — a 1-block fall settles in roughly 7 ticks (~350ms), close enough to vanilla feel without the precise ease-in curve.

**Why so few production bugs found versus how many were expected:** none in the *protocol* (AddActor's shape held up first try, cross-referenced from two independent 2168-ground-truth sources) — the one real bug was in the *physics/landing model*, caught by a test this session wrote specifically to prove the cascade case, not by copying a reference implementation's exact algorithm (Dragonfly ticks real ECS entities with full collision; this is a hand-rolled minimal version for two block types).

**Non-goals:** Anvil/concrete-powder solidify behavior (`Solidifiable`/`damager` in Dragonfly) — sand/gravel only, same scope §57 already committed to; landing damage to entities standing below; a block genuinely lost when its landing cell is claimed by something else mid-fall (best-effort, documented in code, rare in practice); "tunneling" through a single-block-thick floor at high terminal velocity (theoretically possible at ~2 blocks/tick terminal speed, not reachable within Zenith's flat-world fall distances); persisting in-flight falls across a restart (still RAM-only, matches §26's floor-drop precedent).

**Verification:** `dotnet test zenith.sln -c Release`: `GravityTests.cs` rewritten for real multi-tick settling (added a `Settle()` helper that ticks until quiescent, plus a new test proving the source cell vacates before landing completes — the thing that actually distinguishes this from the old instant teleport); 525/525 zenith.Tests green, 637/637 full suite. Live-verified: `zenith-smoke-bot`'s existing `smoke:gravity` (sand fall, 3-block tower cascade, gravel fall — written for §57, never touched for this ADR) passes unchanged against the new implementation, including its 8s wait budgets comfortably covering the new multi-tick fall time.

**Status (ago 2026):** Shipped — `AddActorPacket`, `FallingBlockStore`, rewritten `GravitySystem`, live-verified via `smoke:gravity`.

### 96. Fall damage — first real Health authority, closes the biggest Survival honesty gap

**Superseded in part by §98:** fall-distance policy remains here; authoritative health mutation, death idempotence, peer replication, and atomic death loot now follow §98.

**Choice:** Give `Player.Health` (§40's spawn-attribute field, documented since as "damage pipeline Deferred") its first real write path: fall damage on landing. Before this, the *only* way a player's Health ever changed was void death setting it straight to 0 — there was no damage pipeline of any kind, no `VitalsSystem`, nothing tracking a fall in progress.

1. **`Player.FallPeakY`** — the highest feet Y reached since the player was last on-ground (reset to current Y on every landing and on respawn). Tracked in `MovementSystem.Tick`, using the same `AuthInput.OnGround` (`VerticalCollision`) field the pose-dirty logic already reads — no new wire dependency.
2. **On the `wasOnGround=false → IsOnGround=true` landing transition**, `fallDistance = FallPeakY - PositionY`. Below the vanilla-parity 3-block safe distance, no damage. Past it, `damage = floor(fallDistance - 3)` — one HP per block beyond the safe distance, matching the constant PowerNukkitX/vanilla use.
3. **Lethal fall reuses the exact death handshake void death already has** — extracted `MovementSystem.BeginVoidDeath`'s body into a shared `Kill(player, online, cause)` taking the cause string ("generic" for void, "fall" for this), so `DeathInfoPacket`, Survival death-loot dump, and the Respawn searching/ready sequence are identical regardless of cause. No new death machinery.
4. **Non-lethal damage sends `UpdateAttributesPacket`** via the existing `SendDefaultAttributes` (misleadingly named — it's just "current attributes snapshot", already reused for death) so the client's health bar reflects the hit immediately.
5. **Creative is immune** — matches vanilla's creative fall immunity and Zenith's existing Creative-keeps-inventory convention on death; checked before computing damage, not after.

**Why this is the "biggest gap" closed, not the whole pipeline:** hunger, drowning/air supply, and any damage source other than a fall or the void still do not exist — `Player.Health`'s doc comment is updated to say so explicitly rather than silently going quiet about it. This ADR is deliberately the smallest real slice of the Deferred "VitalsSystem + Player vitals authority" line, not an attempt to close it in one PR.

**Non-goals:** Hunger tick / food; drowning / `AirSupply`; any damage source besides fall and void; fall damage from `MoveActorAbsolute`-driven entities (falling blocks, §95) landing on a player — that's the falling-block side's own `damager` interface (Dragonfly has it for anvils), not implemented; a dedicated `VitalsSystem` GameLoop registration — this lives inline in `MovementSystem` because it only reacts to a transition that system already observes every tick, and a second system re-deriving the same `wasOnGround`/`IsOnGround` comparison would be duplicated state, not real separation.

**Verification:** New `FallDamageTests.cs` (5 tests): short fall no damage, damage scales at 1 HP/block past the safe distance, a lethal fall kills and produces `DeathCause == "fall"`, Creative immunity, and respawn resets `FallPeakY` so the next fall is measured fresh (not against a stale pre-death peak). 530/530 zenith.Tests green, 642/642 full suite. **Not yet live-verified** — no `zenith-smoke-bot` script exercises a real fall yet (unlike gravity/respawn, which already had one); flagged for whoever next touches this path to add one, same honesty stance §90 took for `ItemStackRequestPacket`.

**Status (ago 2026):** Shipped — `Player.FallPeakY`, `MovementSystem.Kill`/`ApplyFallDamage`, 5 new tests. Unit-verified only.

### 97. Choose execution model by behavior, not by available abstraction

**Choice:** Choose the mechanism for a feature from its behavior — `IGameSystem`/tick, immediate runtime call, pending intent consumed on tick, domain event, or scheduled work — rather than from the abstraction already available. The motivating runtime review questioned whether `ChatSystem`/`GameModeSystem`/`EquipmentSystem` belong in `IGameSystem`; the important conclusion was to avoid replacing "everything is a System" with the equally rigid "everything discrete becomes a Command/Event".

1. **Strict invariants (never trade without a new ADR + concrete forcing feature):** transport doesn't decide gameplay; serialization doesn't know domain; packets are wire-only; handlers interpret and call a runtime API, they don't become the decision; authoritative world/player mutation normally has one writer in its gameplay execution context (the tick where ordering matters; exceptions must be explicit and justified); thread ownership is explicit; async work returns a result to the owning context instead of mutating authoritative state itself.
2. **Flexible mechanisms (choose per feature, revisit freely):** `IGameSystem`, Command, Event, Intent/pending-state, Queue, Dirty flag, Scheduler, direct runtime call, Dispatcher, Service. None of these should become a universal rule.
3. **Existing proof the heuristic already works in Zenith:** `Player.SelectedHotbarSlot` (ADR §80) is written directly by the handler with no lock, no queue — because it's a single idempotent, self-correcting scalar. That is the template for "immediate runtime operation," not an exception to feel uneasy about.
4. The full decision heuristic and invariant/mechanism split are in [`docs/adr/0097-runtime-execution-model.md`](adr/0097-runtime-execution-model.md).

**Non-goals:** this ADR does not mandate introducing Command/Event/Scheduler types now — no current feature has ≥2 real consumers or a genuine future-scheduling need beyond what `GravityPendingStore` already covers. It also does not reverse any `IGameSystem` registration: `Chat`/`GameMode`/`Equipment` remain Systems because their ordering guarantees remain useful; only `EquipmentSystem`'s registration order changes.

**Verification:** N/A — this is a decision/heuristic ADR, not a shipped code change. Enforcement is documentation-level: `ARCHITECTURE.md`/`AGENTS.md` updated with the invariant/mechanism split and a pre-implementation checklist (this session).

**Status (ago 2026):** Shipped. `ArchitectureBoundaryTests` protects the mechanically verifiable layer boundaries; the feature-level checklist remains a review responsibility.

### 98. Health is a small authoritative composition primitive

**Choice:** Model health as `HealthState`, composed by `Player` today and available for a later actor without introducing `Entity`, `LivingEntity`, ECS, attributes, effects, or a combat framework.

1. **State and cause are explicit.** `HealthState(maximum)` owns current health, fatal state, and the first `DamageSource`. A source currently carries `DamageCause` (`Generic`, `Fall`, `Void`, `Melee`); it has no attacker identity until a real attribution feature needs one. Invalid amounts (zero, negative, NaN, infinity) are rejected without mutation.
2. **One writer.** `Player.ApplyDamage` is internal and called by the gameplay owner. Handlers, packets, and async continuations do not set health. `DamageResult.Died` is emitted once; later requests are `AlreadyDead`, preserving the first fatal cause.
3. **Death is two deliberate steps.** Health first accepts the fatal transition, then `Player.TryFinalizeDeath` performs the one-time player lifecycle cleanup. This lets the gameplay caller perform concrete death work without making the health leaf know inventories, worlds, packets, or players.
4. **Death loot is atomic at the real boundary.** `FloorDropFanout` plans every craft-grid, bag, and cursor stack before committing any floor cell or clearing a source. Refusal keeps the entire inventory. An unexpected post-plan store failure restores the floor snapshot before it escapes. Memory state is authoritative during the session; persistence is requested only after a successful in-memory transfer.
5. **Replication remains protocol-specific.** The victim receives the existing full local HUD seed. Existing peers receive a focused `minecraft:health` `UpdateAttributes`, and `PlayerVisibility` sends health after `AddPlayer` for late viewers. `UpdateAttributesPacket.CreateHealth` is a packet helper, not an attribute framework.
6. **Respawn starts the next lifecycle.** The gameplay owner applies pose, restores `HealthState` to its configured maximum, then sends local and peer health updates before the normal inventory/UI resync.

**Why:** Fall and void already proved that direct `Player.Health` writes and a cause string were too weak: a second fatal call could perform drops before discovering death had happened, and death loot could clear only the slots that happened to fit. `HealthState` is the smallest shared domain boundary that makes damage validation, fatal idempotence, and future actor composition testable without moving fall policy out of `MovementSystem` or inventing a global `DamageSystem`.

**Non-goals:** armor; hunger; effects; generic attributes; attackers/kill credit; AI; a generic combat engine; entity hierarchy; ECS; plugin hooks. A first actor can compose `HealthState` and apply an explicit `DamageSource.Melee`; adding attacker identity or a new damage policy requires evidence from that feature.

**Verification:** leaf `HealthStateTests` covers controlled melee, invalid input, one lethal transition, and respawn reset. Gameplay tests cover fall, void, conservation across bag/cursor/craft UI, capacity refusal with no partial death loot, and peer health fan-out. The external `smoke:respawn` script currently asserts the obsolete "bag intact" policy, so it is not counted as validation for this change; replace that assertion with an observable death-loot policy and add a peer-health scenario before player-versus-actor gameplay lands.

**Status (ago 2026):** Shipped — no new `IGameSystem`; fall/void remain in `MovementSystem`, which already owns their tick ordering.

## First actor vertical slice (Zombie)

### FIRST_ACTOR_FINDINGS

The first living actor is intentionally a concrete `Zombie` plus a small `ZombieStore` while we
collect evidence for a future actor-runtime decision. The store owns only active authoritative
state; `ZombieSystem` owns spawn, targeting, movement, damage and lifecycle decisions; `HealthState`
owns health transitions; `EntityProtocol` owns Bedrock projection. The store is scaffolding, not a
template for a permanent `SkeletonStore`/`CowStore` family.

| Concern | FallingBlock | FloorDrop | Zombie | Classification |
|---|---|---|---|---|
| Runtime identity | entity/runtime id | item actor id | entity/runtime id | shared lifecycle candidate |
| Position | continuous fall position | cell position | continuous XZ + yaw | similar, not yet generalized |
| Velocity | gravity-specific | none | chase step only | actor-specific |
| Health | none | none | composed `HealthState` | actor-specific |
| Lifetime | short-lived settle/void | delay/age/despawn | active/dead/remove once | similar |
| Active tick | `GravitySystem` | `FloorDropSystem` | `ZombieSystem` | shared execution shape |
| Spawn/remove | gameplay + `EntityProtocol` | gameplay + `EntityProtocol` | gameplay + `EntityProtocol` | repeated plumbing |
| Replication | add/move/remove | add/remove/take | add/health/move/remove | similar, wire-specific |
| Late join | chunk emission | `ColumnSend` | tick-owned viewer catch-up | similar |
| Visibility | known chunk / online | known chunk / online | online viewer set | unknown future boundary |
| World interaction | block support | pickup reach | simple player proximity | actor-specific |

The repeated fields/operations are recorded as evidence only. No `EntityStore`, registry, hierarchy,
ECS, or generic visibility abstraction is introduced until a materially different second actor shows
which parts are actually common. The current vertical slice also records the DX cost explicitly:
one concrete state file, one tick system, one composition-root registration, one attack handoff in
`Player`/handlers, and one actor projection in `EntityProtocol`/`AddActorPacket`.

**Lifecycle invariant:** the bootstrap slice creates at most one actor; the gameplay owner is
the only writer; an accepted melee swing is reach-validated; lethal health removes the actor once;
late ticks cannot resurrect it. This is a testable behavior slice, not a claim that all future actors
share this storage shape.

**Real-client observation:** two Bedrock clients independently received `add_entity` for
`minecraft:zombie` with the same runtime/unique id, then observed actor movement and a health
attribute update. A protocol-valid `InventoryTransaction` `item_use`/`click_air` probe against a
real client then produced the authoritative health sequence `20 → 16 → 12 → 8 → 4 → 0`, followed
by `remove_entity`. The earlier `missed_swing`-only probe was a harness encoding limitation, not a
server-side damage failure. The two-client probe remains useful for replication observation; the
single-client click-air probe closes the real-client damage/death path.
### 99. ECS is an evidence-gated world-actor decision, not the next architecture step

**Choice:** Do not introduce ECS, a generic `WorldEntity` hierarchy, a VisibilitySystem, or a job scheduler as a response to the completed multiplayer spine. First complete the evidence path recorded in [`roadmap.md`](roadmap.md) and the maturity audit: reproducible runtime baseline; general damage/death; first real mob; a materially different second actor; actor lifecycle/replication and visibility requirements; then measured actor workload.

If a spike is opened, it compares the straightforward actor model against an internal archetype/SoA design for real Zenith workloads. It starts single-writer on `GameLoop`, applies only to dynamic world actors (mobs, projectiles, dropped items, falling blocks), and ends in an explicit ADR acceptance **or rejection**. Rejection is valid when the simpler model meets its capacity and maintenance targets.

**Why:** the existing falling-block and floor-item paths are valuable evidence but are not enough to infer common component access patterns. Mature references demonstrate that entity simulation, spatial visibility and parallel scheduling are related but independent costs: Dragonfly maintains chunk viewers and actor ticking separately; PocketMine and PowerNukkitX carry substantial entity lifecycle and extension surfaces. Copying any of those models before Zenith has its own actor pressure would freeze guesses.

**Non-goals:** putting players, inventories, containers, sessions, RakNet, packets/protocol, storage, commands or a public plugin API into ECS by default; using ECS as a substitute for interest management; assuming ECS permits safe parallel execution.

**Verification:** gate checklist and benchmark design in [`audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md`](audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md). No runtime implementation is authorized by this ADR.

**Status (Aug 2026):** Decision recorded; ECS spike not yet open.

### 100. Command surface may grow before plugins, without becoming gameplay authority

**Choice:** `/` commands are a permitted product and developer-experience surface, not a frozen architecture subsystem and not an ECS prerequisite. The command core is protocol-independent: definitions, aliases, typed argument/overload selection, validation, permissions, suggestions and result/error feedback do not reference Bedrock packets. A thin Bedrock adapter translates core definitions/suggestions to client metadata/autocomplete and translates wire input back to the core. Command acceptance delegates to a gameplay runtime API or submits an intent under ADR §97; it never grants a session handler direct authority over world, player, inventory or combat state.

**Why:** a useful command set and client autocomplete solve operator and player UX independently of the still-frozen plugin API. A protocol-free core keeps future Bedrock wire churn from defining domain commands and makes the eventual extension boundary possible without committing to it now. Waiting for plugins would incorrectly couple two decisions; publishing plugin hooks now would freeze an extension contract before gameplay and plugin boundaries are known.

**First proof:** use only a few representative commands, collectively covering aliases; typed arguments; enums; optional arguments; ranges; player targets; multiple syntaxes/overloads; permissions; suggestions/autocomplete; and consistent feedback/errors. The core tests these without Bedrock types; adapter tests prove metadata/autocomplete; a real-client completion smoke verifies the wire edge.

**Non-goals:** a universal command/event framework, plugin command API, `IPluginCommand`, dynamic discovery, DI, public hooks, permission ecosystem clone, or routing gameplay through commands. Start the reusable command layer only when at least two current commands share the need; before then, retain a focused command path such as `/gamemode`.

**Verification:** `CommandCatalogTests`, `BedrockCommandAdapterTests`, and the real-client smoke that decoded `AvailableCommands` and exercised `/gamemode`; future commands must retain the same core/adapter split.

**Status (Aug 2026):** Policy recorded; the first small core/Bedrock slice is implemented and tested. Plugin-facing registration and discovery remain deferred.

### 101. Phase D records actor pressure before any entity-runtime choice

**Choice:** Close the actor-pressure evidence gate with the direct model intact. The second actor is a concrete, short-lived snowball Projectile; it keeps ownership, velocity, collision, impact/lifetime removal and source attribution concrete. The workload runs production single-writer `GameLoop` ordering with real Projectile simulation and protocol/RakNet projection at 100 and 1,000 actors under one and ten observers. It records tail ticks, allocation/GC, churn, actor fan-out and egress. The full matrix, commands and cost decomposition are in [`phase-d-actor-pressure.md`](phase-d-actor-pressure.md).

**Why:** FallingBlock, Zombie and Projectile independently repeat enough active-state, identity, position/lifetime, tick/remove and viewer bookkeeping to justify testing another representation. The 1,000-actor/10-observer p95 (60.556 ms) is above the 20-TPS 50-ms budget while the one-observer path is far lower; that is evidence for separately measuring actor simulation and global replication/interest, not evidence that ECS solves visibility.

**Non-goals:** accepting ECS; `ActorStore`, `EntityStore`, interface hierarchy, component registry, `VisibilitySystem`, pooling framework, jobs/parallel simulation, public actor/plugin API, or command changes. The future experiment remains world-actors only; players, sessions, RakNet, Packets, Protocol, inventory, containers, persistence and commands remain out of scope by default.

**Next decision:** Phase E may compare the direct world-actor model with an internal, single-writer archetype/SoA prototype under the exact creation/removal, collision/damage and observer workloads. It must end with an ADR accept/reject; rejection is valid.

**Status (Aug 2026):** Evidence gate closed; ECS feasibility spike authorized, ECS not accepted.

### 102. Defer world-actor ECS after the first isolated Phase-E comparison

**Choice:** **DEFER ECS.** The isolated direct-list versus contiguous-SoA experiment is recorded in
[`PHASE_E_ECS_FEASIBILITY_FINDINGS.md`](audit/PHASE_E_ECS_FEASIBILITY_FINDINGS.md). It did not
show a material simulation/allocation improvement at 100 or 1,000 mixed actors, while Phase-D
production evidence still identifies observer projection/wire fan-out as the dominant incremental
cost.

**Why:** The prototype intentionally remains incomplete as a runtime candidate: it lacks
generation-safe row mapping and the equivalent Projectile→Zombie query. Its compact removal/churn
is measured, but it creates no demonstrated DX or lifecycle advantage over the direct model.
Accepting a production ECS from that evidence would be preference, not an architecture decision.

**Reopen condition:** an isolated, still single-writer comparison with those missing semantics and
identical packet construction/projection; acceptance still requires material gain beyond wire fan-out.

**Non-goals remain:** no production migration, EntityWorld/registry/public query/components, plugin
API, VisibilitySystem, jobs or parallel scheduler.

### 103. Use confirmed chunk knowledge as the first actor-interest policy

**Choice:** `ActorInterest` is a small Gameplay decision seam, not a `VisibilitySystem`. It
answers only whether an in-game, living observer has confirmed the actor's current chunk through
`PlayerChunkTracker`. Concrete Zombie and Projectile systems retain their own replicated-pair
bookkeeping and reconcile it to send Add on enter, RemoveActor on exit, and movement/health only
to known observers.

**Why:** Phase-D's 1,000 actor / ten observer workload identified replication fan-out and wire
work as the dominant incremental cost; Phase-E deferred ECS. The Phase-F controlled comparison
holds actor state and GameLoop ordering constant and reduces movement fan-out 1,980,000→198,000,
egress 68.85 MB→6.98 MB and mean tick 53.564→8.601 ms when only one of ten observers knows the
actor column. Existing chunk tracking supplies enough information for this concrete requirement.

**Boundary:** ActorInterest does not create packets, mutate gameplay, select damage targets or
control actor lifetime. `EntityProtocol` remains the transmitter. The policy is not a spatial
query API, spatial index, activation system or networking abstraction; actor simulation continues
without observers.

**Non-goals:** ECS, generic VisibilitySystem, octree/spatial tree, ActorStore/EntityStore, public
components/query/plugin APIs and parallel scheduler. Evolve the policy only after a measured,
named interest rule exceeds confirmed chunk knowledge.

**Verification:** focused actor-interest/reconciliation tests plus
[`phase-f-entity-interest.md`](phase-f-entity-interest.md)'s reproducible real-runtime benchmark.

### 104. Runtime health is emitted as bounded server telemetry, not a metrics platform

**Choice:** the server periodically logs one bounded runtime snapshot: tick duration and
over-budget count, connected players, concrete actor count, RakNet sessions and packet bytes,
GC state, and the last graceful persistence-flush duration. RakNet counters are atomic transport
facts; GameLoop only reports elapsed tick duration after the authoritative tick has completed.

**Why:** Phase G needs enough evidence to diagnose beta operation and relate capacity runs to a
50-ms tick budget. This is deliberately cheaper and more honest than inventing an HTTP exporter,
metric registry, event pipeline or new runtime ownership model.

**Boundary:** telemetry neither changes gameplay nor packets, and it does not hold locks across
protocol send, I/O or callbacks. Persistence flush remains shutdown-owned. The reproducible
capacity and recovery limits are recorded in [`phase-g-beta-readiness.md`](phase-g-beta-readiness.md).

**Non-goals:** Prometheus/OpenTelemetry endpoint, dashboard, polling API, generic metric names or
alerting framework. Add an external integration only when an operational consumer requires it.

### 104a. Diagnostics is a fixed-layout leaf, not a server metrics platform

**Choice:** `libs/diagnostics` owns fixed counters, gauges, timings and hierarchical timing
names. Metrics are declared during composition and resolved to array indexes before runtime;
recording performs atomic array updates only. `CaptureSnapshot` creates a read-side immutable
view with console and JSON renderers. The server supplies its own concrete layout for tick/system
cost, gameplay counts, runtime/GC health, packet throughput and RakNet transport facts.

**Why:** beta operations need a lasting way to answer whether latency is in the authoritative
tick, a specific system, allocation/GC, packet fan-out, or UDP egress. A leaf with a fixed layout
keeps measurement available without per-incident logging changes and without teaching the library
about `Player`, `World`, `Packets` or RakNet.

**Boundary:** the diagnostics leaf has no Zenith, gameplay, protocol or transport reference. The
GameLoop records its own and registered-system timings; the session boundary records game packet
counts/bytes; the post-tick observer samples ownership-neutral facts. Snapshot/export is a
reader-side operation. There is no HTTP endpoint, registry mutation after startup, reflection,
global lock, Prometheus/OpenTelemetry adapter, dashboard or plugin API.

**Verification:** `diagnostics.Tests` proves metric/scope/snapshot semantics and zero allocations
after warmup; `GameLoopTests` proves whole-tick and per-system timings; the BenchmarkDotNet
`DiagnosticsOverheadBenchmarks` compares the disabled baseline to the recording path with
allocation diagnostics.

**Maturity addendum:** snapshots carry a build-time category; comparisons and diagnosis execute
only after capture. A fixed-size raw incident ring retains periodic and over-budget samples without
allocating on record; command-driven snapshot/compare/top-timing reads remain concrete
development tooling, not a plugin command framework or monitoring endpoint.

### 104b. Keep PlayerQuitEvent identity-bound; WasInGame is a gameplay-entry predicate

**Choice:** retain one `PlayerQuitEvent` for the teardown of a `Player` whose identity was
accepted and registered in `PlayerManager`. It is published exactly once from
`NetworkSession.HandleClose`, after Session-owned peer removal/persistence handoff and after the
player is removed from `PlayerManager`. `WasInGame` remains the captured fact that the player
completed the spawn transition before that teardown. `PlayerQuitEvent` is not published for a
transport session that fails before a `Player` exists. Do not add `PlayerDisconnectedEvent` now.

**Why:** the two facts are not two independent lifecycle owners or cleanup paths. A transport
close can happen in every login phase; once a Player is bound, all causes (client disconnect,
server kick, timeout, gameplay pre-spawn failure and coordinated shutdown) converge in the same
idempotent RakNet close → listener removal → `HandleClose` path. `WasInGame` answers the single
consumer question that exists today: whether the player was ever made visible to peers, so a
leave chat line is valid. Cleanup/persistence must stay direct and ordered at the Session
boundary, not become event listeners. There is no current second consumer of a disconnection
event that would justify another event type or a lifecycle-event framework.

**Lifecycle audit:** before identity acceptance, authentication/protocol/login rejection leaves
no Player and emits neither event nor persistence. After registration but before spawn, graceful
disconnect, timeout, kick, pre-spawn load failure and shutdown persist stable inventory/player
data, queue chest-opener release for the GameLoop and publish `PlayerQuitEvent(WasInGame=false)`.
After `InGameSessionHandler` enters gameplay, the same path first removes peer visibility and
publishes `PlayerQuitEvent(WasInGame=true)`. UDP/network loss is observed as RakNet timeout;
there is no separate socket-failure callback to model. RakNet's close hook is at-most-once, and
`ZenithSessionListener` removes the session before calling `HandleClose`, preserving exactly-once
game cleanup even if close is requested from more than one context.

**Compatibility:** `PlayerQuitEvent` historically sounded like a completed-gameplay quit, but
its actual established contract is identity-bound teardown; consumers must not infer `InGame`
without checking `WasInGame`. `PlayerLoginEvent` is similarly acceptance/registration-time, not
a spawn-complete event. A future concrete consumer that needs every identity-bound transport
close may add `PlayerDisconnectedEvent` only with that consumer and explicit ordering relative
to this direct cleanup; it must not be used for raw pre-login connections because no `Player`
identity exists.

**Non-goals:** lifecycle hierarchy/base event, generic disconnect reason bus, moving visibility,
chest release or persistence into EventBus listeners, changing PlayerManager ownership, or making
network loss a Gameplay decision.

### 105. Keep two concrete mob behaviors; extract only the Player death transition

**Choice:** retain `ZombieSystem` and `SkeletonSystem` as separate, tick-owned gameplay systems.
Zombie owns target/chase/melee/cooldown decisions; Skeleton owns ranged distance/aim/cooldown
decisions and requests a concrete Projectile from `ProjectileSystem`, which remains the sole
projectile lifecycle owner. `PlayerDamage` reuses the existing Player health, feedback, death-loot
and respawn transition for concrete non-player damage sources.

**Why:** the two behaviors repeat identity, position, target lookup, cooldown and projection
bookkeeping, but their decision state and attack effects remain materially different. The only
duplicated behavior already shared with movement was Player damage/death finalization; extracting
that path prevents a second copy without claiming an entity or combat framework. The Phase-H
benchmark still identifies observer fan-out as the dominant scale cost, not mob simulation.

**Boundary:** Gameplay decides target, damage and lifecycle. `EntityProtocol` transmits add/move/
health/remove outcomes. Packets serialize; RakNet transports. `PlayerDamage` contains no packet
serialization, target selection, actor storage or replication policy.

**Non-goals:** `LivingEntity`/generic actor hierarchy, generic AI/navigation/behavior trees,
combat attributes/effects/armor, ECS, VisibilitySystem, public actor/plugin API or scheduler.

## Explicit non-goals (so far)

Recorded so we don't “accidentally” implement them:

- Plugin API / DI container
- **Replacing** ZLDB with full Mojang LSM on the default path (Mojang open/import → §61 seam + converter instead)
- Multi-level LSM compaction **inside** `Zenith.LevelDB` / PInvoke RocksDB as the product store (unless RAM/streaming need is proven — still not “become BDS”)
- Full creative catalog / `block_state_b64` decode (short CreativeContent list shipped in §31)
- Generic WorldEntity/ECS before ADR §99's evidence gate; broad mob AI before actor foundation (drop-item wire shipped without a generic entity layer — §32)
- Plugin-facing command registration/discovery and public permission hooks; the protocol-independent first slice is allowed by ADR §100
- Protocol bump solely to chase client log version numbers when login already completes
- Actor/EventHandler frameworks copied from other engines
- Generated/centralized packet-dispatch table replacing the per-handler `switch` statements (§76 — wire codegen stops at `Encode`/`Decode`/`Id`, dispatch is a separate ADR if ever pursued)

When a non-goal becomes a goal, update this file **and** `ARCHITECTURE.md`.

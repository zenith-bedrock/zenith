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

**Adendo (jul 2026 — registry maturity):** Dump-driven stance confirmed vs DF/PM/Endstone: full palettes for wire fidelity; curated `Blocks` façade for gameplay; creative/recipes stay separate SSOTs. `BlockPalette` records every `network_id → name` at NBT parse (`TryGetName`); `Blocks.TryGetName` uses curated map then palette reverse so Protocol name→ItemPalette bridge does not air-fallback for in-dump states outside the starter set. `Blocks.IsPlaceable` allowlists curated placeables (+ chest facings); handler + `BlockSystem` reject others. Still **not** PM dual-ID, typed mega-registry, or Blocks→Context (§25).

### 13. GameLoop never waits on LevelDB overlay Put

**Choice:** `World.SetBlock` updates the in-RAM overlay map synchronously, then enqueues persistence (`LevelDbChunkStorage` overlay write queue). No `GetResult` / `.Wait()` on the tick thread.

**Why:** Overlay keys are idempotent (`ov:x:y:z`). Blocking 20 TPS on disk + the shared LevelDB `_gate` stalls every player for one builder's edits — opposite of the GameLoop “light and deterministic” rule.

**Satellites:** column `Get`/`Put` still share `_gate`. Crash mid-queue may lose unshed Puts until `Dispose` drains — best-effort flush on stop. PreSpawn no longer uses `GetResult` (see §14).

### 14. Flat chunk streaming + async PreSpawn

**Choice:** `PlayerChunkTracker` + `ChunkStreamSystem` stream missing columns around the player (async `GetOrCreateColumnAsync`, cap starts/tick). PreSpawn loads the spawn disk via `async` continuation (no receive-thread `GetResult`). Still flat base + overlays — not Mojang worlds / noise gen / VisibilitySystem.

**Adendo (jul 2026 — join overlay catch-up):** PreSpawn `RememberMany` right after LevelChunks (before T0 overlay loop). Live `UpdateBlock` fan-out reaches peers with `Chunks.Knows(cx,cz)` even when `!IsInGame`. On InGame enable set `NeedsOverlayResync`; first `ChunkStreamSystem` tick re-emits current overlays for known columns (`ColumnSend.EmitOverlaysToSession`) so places during the LevelChunk batch window are not lost.

**Why:** Closes the “world dies outside spawn radius” gap without Actor/EventHandlers or vanilla LevelDB. Matches early Zenith spine: continuous flat multiplayer before effects/gen.

**Deferred (conscious):** food/effects, creative inventory, biomes/noise, Actor/EventHandler frameworks (Vedrock early path items we will not mirror).

### 15. Block authority harden + quiet Animate / LevelSoundEvent

**Choice:** Keep Handler → FIFO `BlockEditIntent` → `BlockSystem` for place/break. On tick: re-check bounds, Euclidean reach from **eyes** (`PositionY + PlayerEyeHeight`) with `Player.MaxBlockReach = 6`, place only into air (consume hotbar **after** that check), break only non-air and only if `TryAdd` succeeds (never void items). In-game ACK Animate (`0x2c`) and LevelSoundEvent (`0x7b`) by packet id without DTO fan-out. `MobEquipment` Decode updates `SelectedHotbarSlot` only (place still uses baked intent slot).

**Why:** Closes Fase 3 gaps (occupied place / full-hotbar break / unreachable edits) without Actor or domain EventHandlers. Log spam from swing/sound was WARNING noise, not missing gameplay. Reach is intentionally simple (not AABB/physics).

**Deferred:** AuthInput block path, sound/animate peer fan-out, food/effects. (Inventory storage closed in §16.)

**Adendo (jul 2026 — break path):** Survival destroy arrives via `PlayerAuthInputPacket.BlockActions` when `ServerAuthoritativeBlockBreaking=true` in StartGame. Decode past the position prefix (bitset + flags → `PlayerBlockAction`); map `predict_destroy` (26) → `TrySubmitBreak` / `BlockEditIntent` air. Keep `PlayerAction` (13/26) and `InventoryTransaction` UseDestroy as fallbacks. Progress actions (`start_break` / `crack_break` / `continue_destroy`) ignored until §27.

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

**Why:** No published channel existed for external feedback (Vedrock already had Docker + tag). `zenith.yml` resolves via `AppContext.BaseDirectory` next to the DLL — config cannot live solely under a PMMP-style `/data` that replaces `/app`. Separating `players/` is wrong until playerdata persists (session RAM only today).

**Known debt / principle:** Reinterpreting a former flat LevelDB directory as a data root creates `{path}/worlds/{name}/` empty while orphaning old `CURRENT`/`.ldb` at the root — silent empty world. Detect and **Warning** at boot if root looks like ZLDB and the new world dir is empty/new. Future path-semantics changes must warn loudly, never reinterpret silently (ops visibility vs silent fallback). Relative `world.path` resolves against `AppContext.BaseDirectory` (DLL dir), never process cwd — same as `zenith.yml`.

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

**Adendo (jul 2026 — drop-entity wire MVP):** Each floor cell also holds `EntityRuntimeId` (from `PlayerManager.AllocateRuntimeId`). Spawn → `AddItemActor` (0x0f) to peers with `Chunks.Knows`; pickup → `TakeItemActor` (0x11). Merge count change → `RemoveActor` + `AddItemActor`. Late join / column stream catch-up emits Add for drops in known columns. Position = cell center, velocity zero (floating OK). Still **no** WorldEntity/ECS, gravity tick, despawn TTL, Q-throw, or LevelDB persist of drops (§32).

**Adendo (jul 2026 — AddItemActor ItemStackWrapper):** Protocol 1001 encodes AddItemActor item as legacy **ItemStackWrapper** (same as AddPlayer held), not NetworkItemStackDescriptor. Wrong shape crashed the Bedrock client after full-inv floor drop. `NetworkItemStack` writers renamed to Mojang/PM vocabulary (`WriteItemStackWrapper` / `WriteNetworkItemStackDescriptor` / `WriteItemStack`); packet Encode picks; skip Add when `DescribeStack` is air.

**Adendo (jul 2026 — partial pickup):** `PickupFloorDrops` uses `TryAddUpTo` + `FloorDropStore.TryTakeUpTo`. Partial fit (e.g. stack 63 + floor count 5) takes what fits, `TakeItemActor`, then Remove+Add republish for remainder. `TryAdd` (break/chest dump) stays all-or-nothing with `CaptureSnapshot`/`RestoreSnapshot` so a failed full add never sticky-fills 63→64.

**Adendo (jul 2026 — pickup reach + break partial):** Pickup reach uses item-entity Y (`+0.125`, same as `AddItemActor`) and player torso (`feet + 0.75`), radius **2** blocks — old cell-center `+0.5` / 1.5 from feet failed rim-of-hole smoke. Break/chest dump uses `TryAddUpTo` + floor surplus (not all-or-nothing before drop). `Blocks.SameMergeItem` / `NormalizeMergeRuntimeId` stack chest facings and same-name palette rids. Roll back inventory if `TryTakeUpTo` fails after `TryAddUpTo`.

**Adendo (jul 2026 — AuthInput eye→feet):** StartGame / AuthInput wire Y is eye-space (Vedrock). Domain `Player.PositionY` is feet (`FlatSpawnY`, block reach `+ PlayerEyeHeight`). `InGameSessionHandler` submits via `MovementInputState.FromClientAuthInput` (`eyeY - PlayerEyeHeight`).

**Adendo (jul 2026 — AABB pickup + delay):** Sphere/`torso+0.75` retired. Pickup = player standing AABB (`0.6×1.8` feet) **expanded** `(1, 0.5, 1)` ∩ item AABB `0.25³` at cell (PM/wiki; not DF item-side expand). Pure math in `Geometry/Aabb`; Bedrock sizes in `World/EntityHitboxes`. `FloorDropStore` default **pickup delay 10** ticks (new cell); merge delay = `max(existing, incoming)`; partial `TryTakeUpTo` / republish **does not** reset delay. `TickPickupDelays` once per `BlockSystem` tick before reach checks — same-tick deposit becomes 10→9, not vacuum. Wire still shows item during delay. Expand `+0.5` Y does **not** reach an item one full block below feet (rim-of-hole) — step into the cell / same Y (vanilla-shaped, not the old sphere). **Known debt:** fixed standing BB (no sneak/swim); no off-hand preference; block interact reach stays Euclidean eyes (§15). Proximity gameplay going forward prefers AABB (orientation, not a mandate to rewrite §15 this PR).

### 27. Server-authoritative break timing

**Choice:** Soft blocks use `Blocks.BreakTicks` (empty-hand ≈ hardness×5s @ 20 TPS) snapshotted on AuthInput `start_break`. Same-cell `continue_destroy` does **not** reset the dig timer or re-send `StartCrack` (that finished the crack animation before `SetBlock`). Crack LevelEvent data = `round(65535 / ticks)` (PM/Geyser). Creative InstantBuild skips crack + timing gate. Early/wrong-cell breaks rejected with Debug log.

**Adendo (jul 2026 — dig desync):** Survival `predict_destroy` freezes dig auth (`DigStartedTick` / `DigRequiredTicks`) into `BlockEditIntent`, then `ClearBreakTarget` without StopCrack so same-AuthInput Continue can retarget. `BlockSystem` validates from the intent snapshot, not live `HasBreakTarget`. Reject (break/place) → self `UpdateBlock` of server truth to the breaker only. AuthInput break order: **Abort → Start/Crack → Predict → Continue** (cancel+redig needs Start before Predict; chain-break needs Predict before Continue). Abort always `StopCrack` at Abort packet coords (even when dig already cleared) and does **not** dequeue DigAuthorized intents (`predict` = commit). Same-cell MP: first DigAuthorized in tick order wins loot; loser sees air + Resync — no per-cell dig lock.

**Why:** Instant survival break was an authority hole after AuthInput destroy landed (§15 adendo). Restarting crack on every continue made the animation complete while the server still rejected `predict_destroy`. Live dig + Continue retarget before tick rejected the queued destroy → client ghost + peer desync.

**Deferred:** Tool speed, efficiency enchant, `BLOCK_BREAK_SPEED` (3602) mid-dig updates. (LevelEvent peer fan-out → §42.)

### 28. Chests — RAM store + ISR container 7 (MVP)

**Choice:** Ship the §19 sketch minimally:

1. `ChestStore` dict `(x,y,z) → InventorySlot[27]`; `Ensure` on place; `RemoveAndDump` on break (contents → `TryAdd` else floor drops). No LevelDB `ct:` keys yet.
2. `Player.OpenChest` nullable; set on empty-hand UseClickBlock on chest; clear on `ContainerClose`. Wire: `ContainerOpen` type **0**, window id **2** (≠ `0xff`); `InventoryContent` for chest + player.
3. Flat ISR slots `100+` map from container **7**; `PlayerInventory` does not own chest. `InventorySystem` dual-store transfer/swap with snapshot rollback of player + chest.
4. `PlaceInContainer` / `TakeOutContainer` decode as supported (same shape as take/place).

**Why:** Inventory rearrange (§17) without chests still left Survival “storage furniture” incomplete; dual-store keeps decide≠transmit without stuffing chest into `PlayerInventory`.

**Deferred:** LevelDB hydrate/Put; sneak-to-place-on-chest; double-chest; BlockActor; hopper; full creative item list / `block_state_b64`; window-scoped net-id isolation beyond protocol arrays.

**Known debt (updated §36):** Warn once when Ensure count crosses `10_000` — still no refuse/eviction (needs LevelDB redesign).

### 29. Crafting 2×2 — RecipeRegistry MVP (wire completed in §35)

**Choice (updated Jul 2026):** Ephemeral `PlayerCraftUi` (grid flats `CraftUiBase`+0–3, result `CraftResultFlat`) outside `PlayerInventory` — chest-flat pattern. ISR maps containers 13/60; `CraftRecipe`+`Create`+`Consume` bake into one intent; tick `TryCraftFromGrid` then materialize result. Window **124** UI content on spawn + inventory open. Bag-only `TryCraft` remains for tools/tests. **`CraftRecipe.NumberOfCrafts` honored** (shift-click output): consume ×N, result `out×N` clamped by grid affordability and single-slot `MaxStack` (H0).

**Why:** Empty ISR OK / bag consume never drove SAI craft UI (S35). Grid + Create matches Dragonfly without full 54-slot UI inventory. Discarding times made shift-click Place N×out fail after craft ×1. Skipping CreatedOutput (60) in ItemStackResponse (PM virtual-output habit) desynced sequential take after planks→chest — **fixed:** emit 60 like Dragonfly; refuse Survival craft while Result still occupied.

**Deferred:** 3×3 crafting table; recipe-book **CraftRecipeAuto**; container 14 preview sync; negative stack-net-id prediction; multi-stack CreatedOutput when out×N > MaxStack; double-click gather (no dedicated ISR opcode — client multi Place/Take, intermittent).

### 30. CreativeContent after this spine (conscious yes) — superseded by §31

**Choice (historical):** Prioritize Creative for the **next** feedback-facing milestone after place/break + variety smoke. Recording **yes-next** avoided another silent Deferred cycle.

**Status:** Shipped as §31 (Creative mode v1 + short CreativeContent list). Full `creative_items.json` / `block_state_b64` remains Deferred under §31.

### 31. Creative mode v1 (config → join)

**Choice:**

1. `ServerConfig.Validate()` accepts only `"Survival"` / `"Creative"` (loud fail; no silent fallback).
2. Every joiner gets that single mode from config: `Player.GameMode` plus both `StartGamePacket.GameMode` (PlayerGameMode) and `GameType` (WorldGameMode) from the same config value. Runtime Survival↔Creative is §52 (`/gamemode`); Adventure/Spectator still out.
3. Creative join: empty hotbar (client Creative UI supplies items). Survival: existing starter seed.
4. `CreativeContentPacket` after `ItemRegistry`, before empty BiomeDefinitionList — short list from `ItemPalette` (stone/grass/dirt/planks/log/sand/chest). Do **not** parse `creative_items.json` (`block_state_b64`).
5. `BlockSystem.ApplyEdit` branches: Creative place skips `TryConsumeOne`; Creative break skips timing + inventory/floor loot (still clears chest store). Survival path unchanged.

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

1. **Overlays** (`World._blockOverrides`): `OverrideCount` exposed; one Warning when count crosses `10_000` — **no** refuse SetBlock, **no** eviction.
2. **ChestStore:** one Warning when `Ensure` crosses `10_000` — no silent drop / refuse.
3. **FloorDropStore:** SoftCap `2048` — `TryAddOrMerge` refuses **new** cell keys at cap + Warning once; merging into an existing same-item cell still succeeds.

**Why:** Rule 7 — LAN floor grief is the only bound that is cheap and honest without persistence redesign. Overlays/chests stay document+observe until LevelDB/eviction design exists. No `BoundedStore` framework; no Blocks→Context migrate in this leva (§25 remains).

**Deferred:** Blocks→Context; BoundedStore / VisibilitySystem; overlay eviction with I/O on tick.

**Adendo (jul 2026 — column index):** Secondary `_overlaysByChunk` map updated only via `StoreOverlay` (same path as LevelDB hydrate). `GetOverlaysInColumn` is O(bucket) instead of scanning all overlays. Flat `_blockOverrides` remains SSOT for `GetBlock` / `OverrideCount` / warn@10k. Break→air still overwrites (no TryRemove). Not zero-alloc — result list is still O(column size). No eviction.

### 37. UpdateAbilities + AdventureSettings (spawn seed + fly echo)

**Choice:** After §34 `SetActorData`/`UpdateAttributes`, send `UpdateAbilities` (`0xBB`) then `UpdateAdventureSettings` (`0xBC`) before PreSpawn. Shared `AbilityData` writer (SSOT) also used by `AddPlayer` via wire `GameMode` int. Survival mask = pre-refactor golden bits; Creative = Survival + `MayFly` + `InstantBuild` + `Flying` (join already flying — intentional vs Vedrock). `Invulnerable` off. Runtime id = unique id. Inbound `RequestAbility` (`0xB8`) for `FLYING` only: Creative echoes `SendLocalAbilities` with packet bool (stateless); Survival ignore (no kick). Adventure LAN defaults: ShowNameTags + AutoJump.

**Why:** StartGame gamemode without abilities is cosmetic; clients need ability layers for fly/instant-build feel. RequestAbility must be consumed or Warning-spams. Seed pattern matches §34 — no `Player.MayFly` / AbilitySystem.

**Deferred / next honesty:** hunger/food/fall damage. (`/gamemode` → §52; Death/Respawn → §40 adendo; drop-entity → §26.)

### 38. CraftCreative from CreativeCatalog SSOT

**Choice (updated Jul 2026):** `CreativeCatalog` on `ServerContext` (net ids 1–7). Protocol `BuildCreativeContent` from catalog. ISR `CraftCreative` is a first-class `InventoryStackAction` (same contract as `CraftRecipe`): materialize `MaxStack` into `CraftResultFlat` (CreatedOutput), then honor same-request Place/Take/Drop/Create — Dragonfly `createResults` + `Grow(MaxCount-1)`. `NumberOfCrafts` on CraftCreative is protocol boilerplate (ignored; do not reject times==0). Recipe+Creative in one request rejected. Creative-only (handler + system). Wire `WireTouch` echoes; response omits container 60.

**Why:** Palette vanilla is CreatedOutput→cursor/bag, not bag `TryAdd`. The prior `CraftCreativeNetId` parallel intent with empty Actions discarded client Places (click never reached cursor).

**Deferred:** full `creative_items.json` / `block_state_b64`; ActionDestroy creative trash (Unsupported → whole-request reject today).

### 39. Inventory + chest LevelDB persist (`ct:` / `inv:`)

**Choice:** Same world LevelDB as `c:`/`ov:`. Keys `ct:x:y:z` and `inv:{uuid:D}` with `SlotBlob` version=1 (i32 runtimeId+count pairs). Puts remain fire-and-forget on the tick/network path; LevelDB tracks in-flight chest/inv tasks and `FlushAsync` awaits them on graceful shutdown (§41). World hydrates chests at boot; login `TryLoadInventory` before first content; quit enqueues Put. InMemory storage keeps ct/inv dicts for tests. No `players/` volume (§20).

**Why:** Process restart was wiping bags/chests — ops honesty without Mojang playerdata.

**Deferred:** Ender chest, armor, posição DB, double-chest; `players/` volume.

### 40. Vitals fields + void soft-rescue

**Choice:** `Player.Health`/`Hunger` (20/20) drive `UpdateAttributes` at spawn. Void: if `Y < FlatMinY - 8`, soft-rescue to **world spawn** `(0, FlatSpawnY, 0)` — **no** Health=0 / Respawn handshake (softlock risk). Same-XZ Y-only rescue left players inside dig shafts. Check lives in `MovementSystem` (Rule 7 — no decorative VitalsSystem). Self camera correction uses `MovePlayer` Teleport (§41); `MoveActorAbsolute` alone is insufficient for the local client under client-authoritative movement. Peers still get Absolute via dirty pose fan-out.

**Why:** Attributes literals lied about domain; falling forever was worse than thin vitals. Death wire deferred until respawn protocol is intentional.

**Deferred:** hunger tick, food, fall damage, drowning, death/Respawn packets.

**Adendo (jul 2026 — death / Respawn MVP):** Soft-rescue **superseded** once Respawn wire shipped. Void `Y < FlatMinY - 8` now sets `Health = 0`, `IsDead`, sends `DeathInfo` (`0xbd`) + `Respawn` SEARCHING (`0x2d`); client `Respawn` CLIENT_READY or `PlayerAction` RESPAWN → `SubmitRespawn` → GameLoop restores Health=20, pose to world spawn `(0, FlatSpawnY, 0)`, Teleport + attributes + Respawn READY, then **InventoryContent + UiInventoryContent** (client clears bag UI on death; domain inventory unchanged). **Inventory unchanged** (death drops Deferred — separate leaf). AuthInput / block edits ignored while dead. No VitalsSystem / DamageSystem; logic stays in `MovementSystem` + handler intents. Hunger/food/fall/drowning still Deferred.

### 41. Void MovePlayer Teleport + shutdown persistence flush

**Choice:** Soft-rescue self → `EntityProtocol.SendMovePlayerTeleport` (`MovePlayer` mode Teleport, cause Command). Graceful stop: `IChunkStorage.FlushAsync` (InMemory noop; LevelDB `WhenAll` pending Puts + `DrainOverlayWrites`) via `World.FlushPersistenceAsync`, then dispose storage, then RakNet. `Program` completes the same shutdown path from **CancelKeyPress**, **SIGINT**, and **SIGTERM** (Docker/Dokploy stop) — 5s flush timeout → Warning. Flush **never** on GameLoop tick (ADR §13).

**Adendo (jul 2026 — shutdown DisconnectPacket):** Before LevelDB flush / UDP close, `ZenithSessionListener.DisconnectAll("Server closed")` sends Bedrock `DisconnectPacket` (Immediate) + `FlushOutgoing` then RakNet close (DF/PM — no fixed sleep; DF waits session teardown via WaitGroup, we flush frames synchronously). Abrupt socket death left clients on “host lost” / stuck LAN error UI. MOTD pong matches gophertunnel trailing `0`; RakNet GUID persisted in `server.guid` so LAN identity is stable across restarts.

**Why:** Own camera stayed in void after Absolute-only rescue; Ctrl+C could drop last bag/chest Puts; container SIGTERM previously skipped flush entirely. Operational trust before death/drop-entity surface.

**Smoke:** S39 graceful restart / stop; S40 own-camera snap to world spawn (ARCHITECTURE.md). Ctrl+C with players online → clean disconnect screen.

### 42. Dig crack LevelEvent peer fan-out

**Choice:** Keep `WorldProtocol.SendBlockStartCrack` / `SendBlockStopCrack` session-scoped (Protocol transmits only). Callers use `BlockCrackFanout.Start/Stop(PlayerManager, minerSession, …)` — miner + every other `IsInGame` peer (same Online loop as EquipmentSystem / UpdateBlock). No VisibilitySystem. Dig rarity ≠ §24 hot path.

**Why:** Self-only crack made MP dig look single-player; peers never saw progress. Fan-out is the smallest honest fix without radius culling abstractions.

**Smoke:** S41 (ARCHITECTURE.md). Gate: fail S41 → do not open death/drop-entity.

### 43. Protocol mismatch UX (+ EventBus negotiate seam)

**Choice:** After `NetworkSettings`, `ProtocolGate.Evaluate(client, ServerIdentity.ProtocolVersion)` decides Accepted / FailClient / FailServer. Reject → `LoginProtocol.SendIncompatibleProtocol` (`PlayStatus` 1 or 2) + disconnect; no `Player`. Re-check on `LoginPacket.Protocol`. Before accept/reject, publish mutable `ProtocolNegotiateEvent` (Accepted / RejectPlayStatus) so a future listener can override the gate — **Accepted ≠ second codec** (document: forcing accept without encode support breaks on wire). Multi-codec / YAML supported-protocols / plugins: **Deferred**.

**Adendo (jul 2026 — PlayStatus flush):** Incompatible `PlayStatus` is sent **Immediate** + `NOT_PRESENT` compression, then `FlushOutgoing` before RakNet close. Prior Normal-priority PlayStatus was often lost when `Disconnect` Immediate flushed one random frame and closed — vanilla outdated UI never appeared (DF/PM: PlayStatus only, no custom Disconnect string).

**Why:** Wrong-version clients previously hung or logged in without Bedrock’s classic incompatible UI. Gate stays pure; Protocol only transmits; Handler orchestrates. Cheap multiprotocol seam without inventing PluginAPI now (Rule 7).

**Smoke:** Connect with client protocol ≠ server → Bedrock outdated client/server UI + disconnect.

### 44. Tick peer egress (dirty pose + Online snapshot + GamePacket batch)

**Choice:** `MovementSystem` applies AuthInput then fans Absolute only when pose floats (XYZ+Pitch+Yaw+HeadYaw) differ from `LastReplicated*` (exact equality; look-only must still fan — same AuthInput channel). `PlayerManager.Online` is a single allocating snapshot (`IReadOnlyList`); Tick / nested fan-out capture once (`var online = _players.Online`) and reuse. Dirty movers aggregated per peer via `EntityProtocol.SendMoveAbsolutes`; tick `UpdateBlock`s via `WorldProtocol.PublishUpdateBlocks` — both reuse `SendDataPacket(params)` (one envelope / peer). No VisibilitySystem.

**Why:** Idle AuthInput was O(n²) Absolute spam; repeated Online snapshots nested in loops allocated per mover; one Absolute/UpdateBlock per call wasted RakNet frames. Batch is frame reduction, not a §24 zero-alloc claim.

**Smoke:** Idle LAN — peers idle without Absolute flood; look-turn still updates peer yaw; multi-place same tick → one envelope per peer.

**Adendo (jul 2026 — join settle):** Dirty-check left late joiners with AddPlayer only until the subject moved — peer entities often floated until Absolute. `PlayerVisibility.SendAddPlayer` now follows with `MoveActorAbsolute` (`FLAG_ON_GROUND`) for that recipient so new viewers settle without forcing idle Absolute spam.

### 45. Sparse flat columns (miss without Put)

**Choice:** `GetOrCreateColumnAsync` on storage miss returns shared in-memory flat payload **without** `PutAsync` under `c:x:z`. Existing terrain blobs reused. Legacy empty/corrupt (`!LooksLikeTerrainPayload`) still regenerates flat + Put (disk self-heal). Overlays remain `ov:` only.

**Why:** Exploring virgin chunks previously filled the MemTable with identical flat blobs. Flat authority is procedural + overlay — `c:` was already optional.

**Smoke:** Explore far without LevelDB growth proportional to columns visited; overlays still persist across restart.

### 46. Chest facing only (no double-chest)

**Choice:** Palette secondary index `name + one string-state → network_id` (chest `minecraft:cardinal_direction`). Live place remaps chest item rid → facing from player yaw in `InGameSessionHandler` (front toward player = opposite look). `Blocks.Chest` remains south (item/recipe/creative). `IsChest` covers all four rids; break drops canonical `Blocks.Chest`. `BlockSystem` does not reorient intents submitted as raw south (tests / tools).

**Why:** Adjacent south-south chests client-merge as a visual double while server keeps 2×27 (`ct:`). Correct facing is medium polish without BlockActor / pair model.

**Deferred:** Double-chest (54 slots, sneak-pair, UI); trapped/ender/copper; stairs/beds/doors facing stack.

**Known:** Two adjacent chests with the same cardinal still mesh as a double on the Bedrock client — not a 54-slot container.

**Smoke:** Place chest while looking each cardinal → UpdateBlock rid matches; break always returns stackable south item; open/break works on any facing.

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

**Why:** ~⅔ of the server tree lived under `Network/`, mixing serialize / transmit / session SM — folders contradicted ARCHITECTURE roles and reference projects (Vedrock/PM/gophertunnel).

**Deferred:** Split `World/` (ItemPalette / chests → `Item/` etc.); rename type `NetworkSession`.

### 49. PlayerSkinPacket wire + Session relay (no Player→Packets)

**Choice:** Fix/complete mid-game skin sync: `SerializedSkin` DTOs stay in Packets (`ref BinaryStream` Write/Read, `PieceType` string, list/image caps). `Player` keeps only `SkinRgba`/`SkinWidth`/`SkinHeight` for PlayerList/`SkinWire`. Inbound `PlayerSkinPacket` validates UUID == player, updates RGBA when classic image is well-formed (never null-out on bad image), relays via `PlayerVisibility.RelaySkin` to other InGame peers (Session helper — same join-like orchestration as `AnnounceJoin`; not a SkinSystem/ECS). `IsVerified` relayed as decoded. Login/join persona-complete PlayerList remains Deferred (dual path SkinWire vs full PlayerSkin).

**Why:** Collaborator merge landed broken encode (`BinaryStream` by value), `Player`→Packets leak, self-only echo, and `PieceType` as uint. Chat already forbids handler fan-out loops; skin is rare one-shot so Session helper is enough.

**Smoke:** A changes skin in-game → B sees update; join still SkinWire+RGBA. Spoofed UUID ignored.

**Adendo (jul 2026 — Encode Id):** Outbound `PlayerSkinPacket.Encode` must prefix packet Id (`0x5d`) like every other clientbound DataPacket. Missing Id made peer relay silently ignored by Bedrock (inbound Decode unchanged — header already stripped).

### 50. Split log levels (server vs RakNet)

**Choice:** Two `Logger` instances at composition root (`ZenithServer`), levels from `zenith.yml` `log.server` / `log.raknet`. Aliases (`none`|`info`|`warn`|`error`|`debug`|`all`) map to **severity ladders** on the existing flag enum (`info` = Info|Warning|Error). Defaults ops-first: server `info`, raknet `warn`. YAML without a `log:` block keeps C# defaults (no rewrite of operator files). No Serilog, no categories, no call-site changes in `raknet`.

**Why:** Shared `LogLevel.All` made Bedrock Debug unusable — datagram spam drowned join/place. Transport is mature; gameplay is the active debug surface. Rule 7: two loggers beat a logging framework.

**Smoke:** Default boot — join Info, no `Connected PID` flood. `log.raknet: debug` + `log.server: info` → only transport Debug.

### 51. Collaborator UI/sound/emote packets — wire hygiene (not product feature)

**Choice:** Keep Toast / PlaySound / StopSound / ModalForm / ServerSettings / CloseForm / Emote / EmoteList as Packets DTOs. Fix Encode (packet Id prefix), gophertunnel shapes (SoundPos×8, Emote unsigned runtime + byte flags, EmoteList count/runtime), and expose transmit-only `UiProtocol` + `WorldProtocol.SendPlaySound/StopSound`. Inbound Emote / EmoteList / ModalFormResponse / ServerSettingsRequest stay on the quiet ignore-list (no Systems stub). Round-trips in `zenith.Tests`.

**Why:** Contributor landed DTOs without Id-in-Encode and with broken EmoteList handler decode — unusable outbound and Warning-prone inbound. Wire fix ≠ shipping toast/forms/emote product; H1 smoke stays death/drops.

**Deferred:** Emote peer relay; form intent stack; FormId allocator; product title banners (TextObject / timed UI as gameplay).

**Adendo (jul 2026 — SetTitle wire):** `SetTitlePacket` (0x58) + `UiProtocol.SendTitle` / `SendSubtitle` / `SendActionbar` / `SendTitleTimes` / Clear / Reset shipped as transmit hygiene. Times is a separate packet from text. Inbound ModalFormResponse remains quiet ignore (no Info decode path). Product title use stays Deferred.

### 52. `/gamemode` mínimo (sem command framework)

**Choice:**

1. Inbound `/gamemode` via **`CommandRequest` (0x4D)** (canal Bedrock) ou `Text` chat (fallback). Args: `survival|creative|s|c|0|1`. Outros `/…` quiet ignore (no registry / `/help` / autocomplete).
2. Valid parse → `Player.SubmitGameMode` (overwrite-latest intent). **Never** `SubmitChat` for slash / command lines — `ChatSystem` must not fan-out commands to peers.
3. `GameModeSystem` on tick: `SetGameMode` (private set; inventory **not** reseeded) → Protocol `SetPlayerGameType` + `SendLocalAbilities` + `SendAdventureSettings`; remint `CreativeContent` when switching **to** Creative; Toast feedback via existing `UiProtocol`.
4. Bad `/gamemode` args → same-session Toast (handler decide + transmit; no world mutation). No Gameplay→Packets.

**Why:** H1-3 LAN need — Survival↔Creative in-session without restart/`zenith.yml`. Modes/abilities already exist (§31/§37). Rule 7: one string parse beats a command framework (`dx.md` freeze). Exception to explicit non-goal “`/` commands” — **one** path only, documented here like void-death in MovementSystem.

**Deferred:** Adventure/Spectator; permissions; Bedrock `AvailableCommands` / autocomplete; `/` registry; peer re-AddPlayer on mode change (late viewers already get current `GameMode` on AddPlayer).

**Smoke:** A in Survival `/gamemode creative` → fly/instant dig + Creative UI; `/gamemode survival` → back; peers do not see the slash text in chat; held/dig follow new mode. No `Unhandled Data Packet: 77`.

**Adendo (jul 2026 — CommandRequest canal):** Bedrock 1.26 envia slash como `CommandRequest` (0x4D), não `Text`. Handler parseia `CommandLine` com o mesmo `GameModeConfig.TryParseCommand` (Text `/gamemode` fica fallback). Origin wire = gophertunnel 1001: origin **string** (`player`, …) + UUID + requestId + **Int64** player unique id (sempre). Sem `AvailableCommands` / `CommandOutput` / palette — quiet ignore outros comandos. Não mergear `dev/commands` framework.

### OpenInventory / chest UI (note under §28)

Interact → inventory `ContainerOpen` and chest empty-hand open stay handler→Protocol (same-session UI), not GameLoop intents. Slot mutations stay ISR → `InventoryStackIntent` → `InventorySystem`. Opening a window is transmit of a decided view, not world mutation.

## Explicit non-goals (so far)

Recorded so we don't “accidentally” implement them:

- Plugin API / DI container
- `/` command **framework** + permissions / autocomplete (single `/gamemode` path is §52 — not a framework)
- Mojang LevelDB world format
- Multi-level LSM compaction / PInvoke RocksDB (unless RAM/streaming need is proven)
- Full creative catalog / `block_state_b64` decode (short CreativeContent list shipped in §31)
- WorldEntity / ECS / mob AI (drop-entity **wire** shipped without a generic entity layer — §32)
- Protocol bump solely to chase client log version numbers when login already completes
- Actor/EventHandler frameworks copied from other engines

When a non-goal becomes a goal, update this file **and** `ARCHITECTURE.md`.

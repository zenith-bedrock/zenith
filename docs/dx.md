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

## What we deliberately do **not** ship as DX yet

| Temptation | Why wait |
|------------|----------|
| Plugin API | Wrong extension surface early becomes forever |
| DI container | Hides dependency direction; architecture relies on it being visible |
| Command framework | `/` commands deferred until chat/identity semantics settle |
| “God” event framework | EventBus exists for login/quit; domain consumers wait for need |

Good DX is saying **no** until the yes is cheap to maintain.

## Workflow suggestions

```text
1. Read docs/architecture.md + ARCHITECTURE.md (+ CONTRIBUTING.md / AGENTS.md for PRs and folders)
2. Touch the smallest leaf (`libs/nbt`, `libs/leveldb`, `src/raknet`) when possible
3. Add a unit test before a Bedrock client smoke when the change is format/protocol shape
4. Keep gameplay free of DataPacket / BinaryStream
5. If you need a new abstraction (Factory, ECS, Scheduler): justify against freeze list
6. `PlayerManager.Online` allocates a snapshot — capture once per Tick (`var online = _players.Online`); do not read Online inside a nested loop
7. Folders = roles under `src/zenith/` — look in `Packets/` / `Protocol/` / `Session/`, not a revived `Network/` junk drawer
8. Item wire: `NetworkItemStack` has three writers (`WriteNetworkItemStackDescriptor`, `WriteItemStackWrapper`, `WriteItemStack`). **Packet Encode picks** — never call a “default Write”. AddPlayer/AddItemActor → Wrapper; InventoryContent/MobEquipment → Descriptor; Creative/CraftingData → ItemStack
9. Block→item bridge: Protocol maps via `Blocks.TryGetName` + `ItemPalette` only — reverse lookup must cover the dump (`BlockPalette.TryGetName`), not only curated `Blocks.*` consts. Placeables are an explicit allowlist (`IsPlaceable`), not “any palette rid”
```

Manual smoke expectations (clients A/B, terrain hashes, chat, place/break) live in [`ARCHITECTURE.md`](../ARCHITECTURE.md).

## IDE / tooling

- **.NET 10** solution (`zenith.sln`) with focused test projects
- Nullable + modern C# patterns (`ref struct` streams, `ValueTask` storage)
- YAML for ops config; future player-facing strings planned as TOML (`lang/*.toml`) — not mixed into `zenith.yml`

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
| Palette | Blocks.Stone/Chest/Air (cached fields) | — | ~0 ns (noise floor) | 0 B |
| Palette | ItemPalette.Require (stone/chest/oak_log) | — | ~12–13 ns | 0 B |
| EventBus | Publish | 0 / 1 / 8 listeners | ~4 / ~18 / ~37 ns | 0 B |

**Improvement signals from this run:** ZLIB Deflate on 32-block batches (~6 µs) dwarfs uncompressed encode; `GetOverlaysInColumn` allocates and scales poorly at 10k overlays (~525 KB / ~255 µs) — primary post-alpha RAM scan candidate. Palette `Blocks.*` is already a field hit; item name lookup stays cheap.

## Related

- [Comparison](comparison.md) — when Zenith DX wins vs plugin ecosystems  
- [Why Zenith](why-zenith.md) — long-term bet

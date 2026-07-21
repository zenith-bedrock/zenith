# Zenith — Minecraft Bedrock server software

Zenith is a modern Bedrock Edition server written in **C# / .NET**, built around a strict split between **gameplay decisions**, **protocol transmission**, and **wire serialization**.

## Status

**Early development** — not production-ready. Core networking, session flow, flat world + overlays, chat, visibility, config, owned NBT/LevelDB libraries are in progress. Feedback and contributions welcome.

## Documentation

| Start here | |
|------------|---|
| [docs/README.md](docs/README.md) | Doc hub |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Issues, PRs, contribution norms |
| [AGENTS.md](AGENTS.md) | Folder layout (humans & agents) |
| [docs/architecture.md](docs/architecture.md) | Layered system map |
| [docs/decisions.md](docs/decisions.md) | Why we made key choices (commit-backed) |
| [docs/roadmap.md](docs/roadmap.md) | Lean implementation horizons (not PM parity) |
| [docs/comparison.md](docs/comparison.md) | Vs PocketMine, Nukkit, BDS, … |
| [docs/dx.md](docs/dx.md) | Developer experience |
| [docs/why-zenith.md](docs/why-zenith.md) | Long-term bet |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Living engineering rules (constraints) |
| [libs/leveldb/README.md](libs/leveldb/README.md) | `Zenith.LevelDB` notes |

## Technical references (transparency)

Comments and ADR tags (`ADR §N`, `§N`) appear in many files. **They are pointers, not a second spec.** When code and prose disagree, **`ServerIdentity` + `ServerConfig.Validate()` + `ARCHITECTURE.md`** win for runtime; **[`docs/decisions.md`](docs/decisions.md)** wins for intent and history.

### What this build speaks (SSOT)

| Field | Value | Defined in |
|-------|--------|------------|
| Bedrock protocol | **1001** | [`src/zenith/Server/ServerIdentity.cs`](src/zenith/Server/ServerIdentity.cs) |
| Game version (wire) | **1.26.33** | same |
| Product / release | **0.0.2-alpha** | same (`ProductVersion` — Docker tags, logs; not on Bedrock wire) |

New encode/decode work must match **1001**, not the newest Mojang docs tip (often **2169** / **1.26.50** on branch `r/26_u4`). Bump workflow: [`docs/protocol-churn.md`](docs/protocol-churn.md).

### Authoritative docs inside this repo

| Document | Use for |
|----------|---------|
| [`docs/decisions.md`](docs/decisions.md) | **ADR §1–§67** — every `ADR §N` / `§N` in code should resolve here |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Layer rules, smoke expectations, freeze list |
| [`AGENTS.md`](AGENTS.md) | Folder roles (`Gameplay/` decides, `Protocol/` transmits, …) |
| [`docs/dx.md`](docs/dx.md) | Contributor workflow, protocol-doc usage, local reference clones |
| [`docs/roadmap.md`](docs/roadmap.md) | What is shipped vs deferred (not PM parity) |
| [`schemas/zenith.schema.json`](schemas/zenith.schema.json) | **IDE autocomplete only** — boot validation is `ServerConfig.Validate()` |
| [`deploy/README.md`](deploy/README.md) | Docker `/data` volume, Dokploy pitfalls |

### Shipped assets (embedded, not fetched at runtime)

| Asset | Role |
|-------|------|
| [`src/zenith/data/block_palette.nbt`](src/zenith/data/block_palette.nbt) | Block runtime IDs (`Blocks.Load`) |
| [`src/zenith/data/item_palette.json`](src/zenith/data/item_palette.json) | Item registry wire + tools |
| [`src/zenith/data/creative_items.json`](src/zenith/data/creative_items.json) | Reference dump — **not** fully parsed at boot (short curated list in code) |

Curated gameplay subsets (`Blocks`, `DigProfiles`, `CreativeCatalog`) are intentional — full registry fidelity is wire via palettes, not every block is placeable/diggable yet.

### External references (study only — **not** vendored, **not** runtime deps)

We read these for wire shapes, ops patterns, and gameplay math. Zenith does **not** fork or bundle them.

| Reference | Typical use in Zenith | Upstream |
|-----------|----------------------|----------|
| **Mojang bedrock-protocol-docs** | Packet field trees, changelogs | [github.com/Mojang/bedrock-protocol-docs](https://github.com/Mojang/bedrock-protocol-docs) branch **`r/26_u4`** |
| **PocketMine-MP / BedrockProtocol** | AuthInput bit indices, 1001-era cross-check | [github.com/pmmp/PocketMine-MP](https://github.com/pmmp/PocketMine-MP) |
| **Endstone** | BDS-aligned headers, Docker `/data` UX pattern | [github.com/EndstoneMC/endstone](https://github.com/EndstoneMC/endstone) |
| **Dragonfly (Go)** | `BreakDuration` / dig timing formulas | [github.com/df-mc/dragonfly](https://github.com/df-mc/dragonfly) — see `break_info.go`, Mojang `BlockBreakingOverview.md` (ADR §27) |
| **Prismarine bedrock-protocol** | Smoke bot only (separate repo) | [github.com/PrismarineJS/bedrock-protocol](https://github.com/PrismarineJS/bedrock-protocol) |
| **BDS / Mojang world format** | **Deferred** — ADR §61 seam + converter, not in-process decode | Official dedicated server layout |

Local clone layout (optional dev setup): [`docs/dx.md`](docs/dx.md) § “Bedrock references” — e.g. `~/Development/references/bedrock/`.

### Related Zenith repos

| Repo | Role |
|------|------|
| [zenith-bedrock/zenith](https://github.com/zenith-bedrock/zenith) | This server (C#) |
| [zenith-bedrock/zenith-smoke-bot](https://github.com/zenith-bedrock/zenith-smoke-bot) | Bun + bedrock-protocol join/place smoke (ADR §58) — **not** in this tree |

### What we copy vs what we own

| Own (LGPL in-repo) | Borrow concept / cross-check only |
|--------------------|-----------------------------------|
| `src/raknet`, `libs/nbt`, `libs/leveldb` | PocketMine-style auth modes (`self-signed`, `offline`) |
| Packet encode for protocol **1001** | Dragonfly dig duration math |
| ZLDB overlay world model (`c:`/`ov:`/`inv:`/`ct:`/`pd:`) | Endstone/PocketMine **single data directory** Docker pattern |
| Native terrain gen (§63–§67) | **Not** BDS noise/biome tables, **not** PM plugins |

**Explicit non-goals** (do not assume from comments elsewhere): plugin API, full Mojang `creative_items.json` / `block_state_b64`, in-process BDS world import, ECS/mob AI, `/` command framework — listed in [`docs/decisions.md`](docs/decisions.md) § “Explicit non-goals”.

### Reading `ADR §N` in source

Example: `// ADR §57` → open [`docs/decisions.md`](docs/decisions.md) and search `### 57.` or `§57`. Shorthand `§N` in comments is the same index. New layers or abstractions need a **new ADR section before merge**, not only a code comment.

## Solution layout

```
libs/
  nbt/           Zenith.Nbt (reusable leaf)
  leveldb/       Zenith.LevelDB (reusable leaf)
  *.Tests/
src/
  zenith/        Game server (Gameplay, Packets, Protocol, Session, World, Player, Server, …)
    data/        Embedded palettes (block_palette.nbt, item_palette.json, …)
  raknet/        Reliable UDP transport
  *.Tests/
deploy/          Docker /data volume sample + Dokploy guide — not used by dotnet run
docs/            Narrative documentation
workspace/       Local agent/dev scratch (not product)
```

## Build / run

```bash
dotnet build zenith.sln
dotnet test zenith.sln
dotnet run --project src/zenith
```

### Docker (LAN alpha)

```bash
docker compose up --build
```

Persistent **`/data`** volume (config + worlds) — same idea as PocketMine/Endstone. See [`deploy/README.md`](deploy/README.md) for Dokploy. Local dev bind mount: `deploy/docker-compose.override.example.yml`. Product version: `ServerIdentity.ProductVersion`. Empty `world.path` in non-Docker runs = InMemory (volatile).

### Benchmarks (optional)

```bash
dotnet run -c Release --project src/zenith.Benchmarks -- -f * -j short -m --join
```

Numbers live in [`docs/dx.md`](docs/dx.md).

Optional pack of leaf libraries (not published):

```bash
dotnet pack libs/nbt/nbt.csproj -c Release -o artifacts
dotnet pack libs/leveldb/leveldb.csproj -c Release -o artifacts
```

## License

[LGPL-3.0](LICENSE)

---

Create the future of Minecraft Bedrock servers with Zenith — with layers that survive the next protocol bump.

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
deploy/          Docker compose host sample (zenith.yml + worlds mount) — not used by dotnet run
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
mkdir -p deploy/worlds
docker compose up --build
```

Mounts: `./deploy/zenith.yml:/app/zenith.yml` and `./deploy/worlds:/app/worlds` (ops sample only — not used by `dotnet run`). Sample config uses `world.path: /app` → LevelDB at `/app/worlds/world`. Empty `world.path` = InMemory (volatile). Product version: `0.0.1-alpha` (`ServerIdentity.ProductVersion`). Gate: [`docs/alpha-gate.md`](docs/alpha-gate.md).

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

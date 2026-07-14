# Zenith — Minecraft Bedrock server software

Zenith is a modern Bedrock Edition server written in **C# / .NET**, built around a strict split between **gameplay decisions**, **protocol transmission**, and **wire serialization**.

## Status

**Early development** — not production-ready. Core networking, session flow, flat world + overlays, chat, visibility, config, owned NBT/LevelDB libraries are in progress. Feedback and contributions welcome.

## Documentation

| Start here | |
|------------|---|
| [docs/README.md](docs/README.md) | Doc hub |
| [docs/architecture.md](docs/architecture.md) | Layered system map |
| [docs/decisions.md](docs/decisions.md) | Why we made key choices (commit-backed) |
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
  zenith/        Game server (Gameplay, Network, World, …)
    data/        Embedded palettes (block_palette.nbt, item_palette.json, …)
  raknet/        Reliable UDP transport
  *.Tests/
docs/            Narrative documentation
workspace/       Local agent/dev scratch (not product)
```

## Build / run

```bash
dotnet build zenith.sln
dotnet test zenith.sln
dotnet run --project src/zenith
```

Optional pack of leaf libraries (not published):

```bash
dotnet pack libs/nbt/nbt.csproj -c Release -o artifacts
dotnet pack libs/leveldb/leveldb.csproj -c Release -o artifacts
```

## License

[LGPL-3.0](LICENSE)

---

Create the future of Minecraft Bedrock servers with Zenith — with layers that survive the next protocol bump.

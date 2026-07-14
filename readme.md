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
| [leveldb/README.md](leveldb/README.md) | `Zenith.LevelDB` notes |

## Solution layout

```
zenith/     Game server (Gameplay, Network, World, …)
raknet/     Reliable UDP transport
nbt/        Zenith.Nbt
leveldb/    Zenith.LevelDB
*.Tests/    xUnit projects
docs/       Narrative documentation
```

## License

[LGPL-3.0](LICENSE)

---

Create the future of Minecraft Bedrock servers with Zenith — with layers that survive the next protocol bump.

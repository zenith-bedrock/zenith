<p align="center">
  <b>Zenith</b> — a modern Minecraft: Bedrock Edition server, written in C# / .NET
</p>

<p align="center">
  <a href="https://github.com/zenith-bedrock/zenith/actions/workflows/ci.yml"><img src="https://github.com/zenith-bedrock/zenith/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <a href="https://github.com/zenith-bedrock/zenith/releases/latest"><img alt="GitHub release" src="https://img.shields.io/github/v/release/zenith-bedrock/zenith?label=release&include_prereleases"></a>
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/github/license/zenith-bedrock/zenith"></a>
</p>

Zenith is built around a strict split between **gameplay decisions**, **protocol transmission**, and **wire serialization** — layers that survive the next protocol bump instead of one big packet-handler blob.

## Status

**Early / alpha** — not production-ready. Core networking, session flow, flat + generated world with overlays, chat, visibility, config, and owned NBT/LevelDB libraries are working. Feedback and contributions welcome. See [docs/vanilla-behavior.md](docs/vanilla-behavior.md) for what's safe to test today.

## :warning: Zenith is not vanilla-complete yet

It does not have mobs/AI, redstone, hunger, full damage, enchantments, or a plugin API yet — see [docs/roadmap.md](docs/roadmap.md) for what's shipped vs planned. If you want vanilla parity today, use [official BDS](https://minecraft.net/download/server/bedrock). If you want a mature plugin ecosystem today, use [PocketMine-MP](https://github.com/pmmp/PocketMine-MP) or [Nukkit](https://github.com/CloudburstMC/Nukkit). Zenith is for people who want to **own** the server and are fine with early-stage gaps — see [docs/comparison.md](docs/comparison.md) for the full picture.

## Quick start

```bash
git clone https://github.com/zenith-bedrock/zenith
cd zenith
dotnet run --project src/zenith
```

Requires the .NET SDK matching `zenith.sln`. Default port is `19132/UDP`.

### Docker (LAN alpha)

```bash
docker compose up --build
```

Persistent **`$ZENITH_DATA`** volume holds config + worlds. Deploy contract: [deploy/README.md](deploy/README.md).

### Tests / benchmarks

```bash
dotnet test zenith.sln
dotnet run -c Release --project src/zenith.Benchmarks -- -f * -j short -m --join
```

## Documentation

| Start here | |
|------------|---|
| [docs/README.md](docs/README.md) | Doc hub — full index |
| [docs/vanilla-behavior.md](docs/vanilla-behavior.md) | Tester guide: what should match vanilla, what's known-different |
| [docs/roadmap.md](docs/roadmap.md) | What's shipped, what's next, explicit non-goals |
| [docs/architecture.md](docs/architecture.md) | Layered system map |
| [docs/comparison.md](docs/comparison.md) | Vs PocketMine, Nukkit, BDS, … |
| [docs/technical-reference.md](docs/technical-reference.md) | Protocol version SSOT, embedded assets, external study refs |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Living engineering rules (constraints) |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Issues, PRs, contribution norms |
| [AGENTS.md](AGENTS.md) | Folder layout (humans & agents) |

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
deploy/          Product image + Compose/Dokploy + Pterodactyl adapters — not used by dotnet run
docs/            Narrative documentation
workspace/       Local agent/dev scratch (not product)
```

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for issue/PR norms and [AGENTS.md](AGENTS.md) for the folder layout. Pick work from [docs/roadmap.md](docs/roadmap.md) "Yes-next" rather than opening large new subsystems ahead of an ADR.

## License

[LGPL-3.0](LICENSE)

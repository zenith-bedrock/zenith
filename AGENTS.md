# Zenith — agent guide

**Philosophy:** who decides ≠ who transmits ≠ who serializes. Read [`ARCHITECTURE.md`](ARCHITECTURE.md) before changing layers.

## Where code lives (`src/zenith/`)

| Folder | Role |
|--------|------|
| `Gameplay/` | Decide + GameLoop / systems |
| `World/` | Terrain, Blocks, palettes, chests, floor drops |
| `Player/` | Player, intents, inventory, manager |
| `Server/` | Boot, config, identity, context |
| `Packets/` | Wire models — **serialize only** (no Server/World/Player) |
| `Protocol/` | Transmit already-decided outcomes (`*Protocol`, ColumnSend, ProtocolGate) |
| `Session/` | Inbound SM — handlers, NetworkSession, ZenithSessionListener, PlayerVisibility |
| `Event/` `Log/` `data/` | Infra / embedded assets |

**Do not recreate `Network/`** as a catch-all. Leaf libs (`libs/nbt`, `libs/leveldb`, `src/raknet`) stay separate and never reference `zenith`.

## Rules & docs

- Always-on: [`.cursor/rules/zenith-architecture.mdc`](.cursor/rules/zenith-architecture.mdc), [`.cursor/rules/folder-layout.mdc`](.cursor/rules/folder-layout.mdc)
- Intent/tick, raknet, testing: other files under `.cursor/rules/`
- Decisions: [`docs/decisions.md`](docs/decisions.md) — ADR **before** a new layer/abstraction
- DX: [`docs/dx.md`](docs/dx.md)

Frozen: Scheduler, Actor/ECS, VisibilitySystem, DI, plugin API, `/` commands — unless a concrete feature forces them (record in decisions first).

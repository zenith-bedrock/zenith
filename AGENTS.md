# Zenith — agent guide

**Philosophy:** who decides ≠ who transmits ≠ who serializes. Read [`ARCHITECTURE.md`](ARCHITECTURE.md) before changing layers.

## Where code lives (`src/zenith/`)

| Folder | Role |
|--------|------|
| `Gameplay/` | Decide + GameLoop / systems |
| `World/` | Terrain, Blocks, palettes, chests, floor drops |
| `Geometry/` | AABB / spatial math pura (sem World/Player) |
| `Player/` | Player, intents, inventory, manager |
| `Server/` | Boot, config, identity, context |
| `Packets/` | Wire models — **serialize only** (no Server/World/Player) |
| `Protocol/` | Transmit already-decided outcomes (`*Protocol`, ColumnSend, ProtocolGate) |
| `Session/` | Inbound SM — handlers, NetworkSession, ZenithSessionListener, PlayerVisibility |
| `Event/` `Log/` `data/` | Infra / embedded assets |

**Do not recreate `Network/`** as a catch-all. Leaf libs (`libs/nbt`, `libs/leveldb`, `src/raknet`) stay separate and never reference `zenith`.

## Rules & docs

- Always-on: [`.cursor/rules/zenith-architecture.mdc`](.cursor/rules/zenith-architecture.mdc), [`.cursor/rules/folder-layout.mdc`](.cursor/rules/folder-layout.mdc), [`.cursor/rules/naming.mdc`](.cursor/rules/naming.mdc)
- Intent/tick, raknet, testing: other files under `.cursor/rules/`
- **Agent skills (invoke explicitly):** `zenith-post-impl-audit` — audit git status/diff vs architecture/DX after implementation; `zenith-unit-tests` — add leaf unit tests when the diff warrants them. Same content under [`.cursor/skills/`](.cursor/skills/), [`.claude/skills/`](.claude/skills/), [`.opencode/skills/`](.opencode/skills/)
- Decisions: [`docs/decisions.md`](docs/decisions.md) — ADR **before** a new layer/abstraction
- **Technical references:** [`readme.md`](readme.md#technical-references-transparency) — protocol SSOT, ADR § index, external study repos (PM/DF/Endstone/Mojang), what is owned vs borrowed
- Roadmap: [`docs/roadmap.md`](docs/roadmap.md) — horizon order (alpha first; no PM parity chase)
- DX: [`docs/dx.md`](docs/dx.md) — includes how to use Mojang `bedrock-protocol-docs` (`r/26_u4`) vs Zenith protocol **1001**
- Naming: [`docs/naming.md`](docs/naming.md) — verb prefixes (`Handle`/`Apply`/`Try`/`Send`/`Relay`…); **no `Maybe*`**
- Platform health (not H1 product): [`docs/robustness-dx-debt.md`](docs/robustness-dx-debt.md) + ADR §54 — includes **critical delivery risks** (dirty&gt;remote, store SoftCap, leaf CI vs Bedrock E2E)
- Protocol smoke bot (ADR §58): [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) — Bun + bedrock-protocol; **not** inside this C# repo
- **Delivery:** if you close an audit/hygiene gap, **commit + push the same day** — dirty tree ahead of `origin` is a process failure, not WIP

Frozen: Scheduler, Actor/ECS, VisibilitySystem, DI, plugin API, `/` commands — unless a concrete feature forces them (record in decisions first).

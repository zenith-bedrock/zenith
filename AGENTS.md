# Zenith — agent guide

**Philosophy:** who decides ≠ who transmits ≠ who serializes. Read [`ARCHITECTURE.md`](ARCHITECTURE.md) before changing layers.

## Where code lives (`src/zenith/`)

| Folder | Role |
|--------|------|
| `Gameplay/` | Decide + GameLoop / systems |
| `World/` | Terrain, Dimension, Blocks, palettes, chests, floor drops |
| `Geometry/` | AABB / spatial math pura (sem World/Player) |
| `Player/` | Player, intents, inventory, manager |
| `Server/` | Boot, config, identity, context |
| `Packets/` | Wire models — **serialize only** (no Server/World/Player) |
| `Protocol/` | Transmit already-decided outcomes (`*Protocol`, ColumnSend, ProtocolGate) |
| `Session/` | Inbound SM — handlers, NetworkSession, ZenithSessionListener, PlayerVisibility |
| `Event/` `Log/` `data/` | Infra / embedded assets |

**Do not recreate `Network/`** as a catch-all. Leaf libs (`libs/nbt`, `libs/leveldb`, `src/raknet`) stay separate and never reference `zenith`.

**Single-writer gameplay ownership:** authoritative `Player`/`World`/inventory/combat state is
normally written only by its owning gameplay execution context (currently the GameLoop). Network
and async work publish bounded immutable intents/results; locks belong at those handoff boundaries,
not as permission for shared domain writers. This does not prohibit concurrent I/O, networking,
compression, storage, caches, metrics, or session infrastructure. Never hold a lock across await,
I/O, protocol send, or external callback.

**Correctness-critical state:** do not claim “anti-dup” generically. Define and test the concrete
invariant: duplicate/stale/failed/cancelled input must not create extra authoritative state or
leave a partial commit. Use generation/request IDs/snapshots only where a real transition needs
them; for resource movement, test conservation across all participating stores.

## Rules & docs

- Always-on: [`.cursor/rules/zenith-architecture.mdc`](.cursor/rules/zenith-architecture.mdc), [`.cursor/rules/folder-layout.mdc`](.cursor/rules/folder-layout.mdc), [`.cursor/rules/naming.mdc`](.cursor/rules/naming.mdc)
- Intent/tick, raknet, testing: other files under `.cursor/rules/`
- **Agent skills (invoke explicitly):** `zenith-post-impl-audit` — audit git status/diff vs architecture/DX after implementation; `zenith-unit-tests` — add leaf unit tests when the diff warrants them. Same content under [`.cursor/skills/`](.cursor/skills/), [`.claude/skills/`](.claude/skills/), [`.opencode/skills/`](.opencode/skills/)
- Decisions: [`docs/decisions.md`](docs/decisions.md) — ADR **before** a new layer/abstraction
- **Technical references:** [`docs/technical-reference.md`](docs/technical-reference.md) — protocol SSOT, ADR § index, external study repos (PM/DF/Endstone/Mojang), what is owned vs borrowed
- Roadmap: [`docs/roadmap.md`](docs/roadmap.md) — H1 closed; pick from **Yes-next** (no PM parity chase)
- Refs habit: [`docs/dx.md`](docs/dx.md) — Dragonfly essentials + PocketMine issues as footgun catalog (not plugin/command framework copy)
- DX: [`docs/dx.md`](docs/dx.md) — includes how to use Mojang `bedrock-protocol-docs` (`r/26_u4`) vs Zenith protocol **2169** (ADR §79 — ~23 packets still pre-Cereal, tracked debt)
- Naming: [`docs/naming.md`](docs/naming.md) — verb prefixes (`Handle`/`Apply`/`Try`/`Send`/`Relay`…); **no `Maybe*`**
- Platform health (not H1 product): [`docs/robustness-dx-debt.md`](docs/robustness-dx-debt.md) + ADR §54 — includes **critical delivery risks** (dirty&gt;remote, store SoftCap, leaf CI vs Bedrock E2E)
- Protocol smoke bot (ADR §58): [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) — Bun + bedrock-protocol; **not** inside this C# repo
- **Delivery:** if you close an audit/hygiene gap, **commit + push the same day** — dirty tree ahead of `origin` is a process failure, not WIP

Frozen: Scheduler, Actor/ECS, VisibilitySystem, DI, plugin API, `/` commands — unless a concrete feature forces them (record in decisions first).

## Before implementing a new gameplay feature (ADR §97)

`IGameSystem` is a tool, not a mandatory boundary. Do not default to "gameplay feature → create a System" or "discrete action → create a Command + Handler + Event" without justification — both are equally rigid, just with different names. Read [`docs/adr/0097-runtime-execution-model.md`](docs/adr/0097-runtime-execution-model.md) before choosing a mechanism, and answer these internally first:

1. What triggers this behavior?
2. Who owns the authoritative state?
3. Does it require tick execution (continuous evaluation, ordering against other gameplay this tick)?
4. Does ordering against other gameplay matter?
5. Does it need temporary/pending state — or can it call a runtime API directly (see `Player.SelectedHotbarSlot`, ADR §80, for the existing direct-call template)?
6. Does it need cross-thread synchronization — and is that concurrency inherent to the problem (real-time network arrival, async I/O) or created by the mechanism you're about to add?
7. Can it execute directly inside the gameplay runtime, synchronously, from the handler?
8. Is a new abstraction (Command/Event/Scheduler/Dispatcher) justified by ≥2 real, current use cases — not a hypothetical future one?
9. How will this be tested — does it need a substitutable seam, or does a direct test of the method already suffice?
10. How does the closest reference implementation (PocketMine/Dragonfly/Minestom/Cuberite) handle this behavior, and is that technique applicable, partially applicable, or inadequate given Zenith's constraints (single-thread tick, C#, no ECS)?

Do not create an `IGameSystem` merely to move work out of a packet handler. Preserving the gameplay/network boundary does not imply deferring every operation to the next tick. Prefer the simplest execution model that preserves authority, ordering, thread ownership, and testability.

# Architecture

> One-line philosophy: **who decides ≠ who transmits ≠ who serializes.**

This page explains the shape of Zenith. The authoritative constraint list is [`ARCHITECTURE.md`](../ARCHITECTURE.md).

## System map

```
┌─────────────────────────────────────────────────────────────────┐
│ zenith (game server)                                            │
│  Gameplay (decide)  →  Protocols (transmit)  →  Packets (wire)  │
│  Handlers (session SM)     GameLoop / Systems (tick)            │
│  World / Inventory / Player                                     │
└───────────────┬─────────────────────────────┬───────────────────┘
                │                             │
         ┌──────▼──────┐               ┌──────▼──────┐
         │ Zenith.Nbt  │               │Zenith.LevelDB│
         │ LE/Network/ │               │ mem+WAL+SST  │
         │ BigEndian   │               │ c: / ov: keys│
         └─────────────┘               └─────────────┘
                │
         ┌──────▼──────┐
         │   raknet    │  UDP reliable transport (BinaryStream ref struct)
         └─────────────┘
```

Dependency direction is always **down**: gameplay never references `DataPacket`; packets never reference `Player`/`World`; Nbt and LevelDB have **no** references to zenith or raknet.

Folders under `src/zenith/` match roles: `Gameplay/`, `World/`, `Player/`, `Server/`, `Packets/`, `Protocol/`, `Session/` (plus `Event/`, `Log/`, `data/`). Do **not** recreate a `Network/` catch-all — see Layout in [`ARCHITECTURE.md`](../ARCHITECTURE.md) and ADR §48.

## Roles

| Role | Responsibility |
|------|----------------|
| **Gameplay** | Decisions: world, inventory, entities, rules. Owns runtime (`GameLoop`, systems). |
| **Handler** | Connection/inbound state machine. Decodes, validates (NaN/Inf), records **intent only**. |
| **Protocol** | Turns already-decided intents into sends. Does not pick visibility or chunk sets as policy. |
| **Packets** | Bedrock wire models + serialize/deserialize. |
| **RakNet** | Reliable UDP. Separate “network tick” from game tick. |

## Inbound vs outbound

### Outbound

```
Gameplay → Protocol → Packets → (compression) → RakNet
```

### Inbound (gameplay-affecting)

```
RakNet thread
  → Handler
  → pending intent (e.g. movement / place)
  → GameLoop Tick
  → System (MovementSystem, BlockDigSystem, BlockEditSystem, …)
  → Protocol → RakNet
```

The network thread **must not** mutate authoritative gameplay state (final position, inventory, world). Mutation happens on the tick so behavior stays deterministic and debuggable.

## GameLoop

- Target **20 TPS**; advances `GameClock`; invokes `IGameSystem` in **registration order**.
- Exception in one system is logged; the loop continues (same isolation idea as EventBus listeners).
- **Single-threaded** until a concrete feature forces parallelism — no Scheduler / Actor / ECS “for cleanliness.”

## Persistence model (world)

| Concern | Approach |
|---------|----------|
| Base terrain | Flat overworld payloads (`ChunkPayloads`), FNV `network_id` hashes |
| Edits | Sparse overlays in LevelDB (`ov:x:y:z`) + in-RAM map; warn-once at overlay/chest thresholds; floor SoftCap refuse (ADR §36); `UpdateBlock` to clients |
| Columns | Optional `c:x:z` — miss não materializa flat (ADR §45); legacy empty ainda migra via Put; `IChunkStorage` is `ValueTask`-first |
| StartGame | `UseBlockNetworkIdHashes = true` so client decodes palette hashes correctly |

We intentionally **do not** rewrite full subchunks on every place/break. Overlay-first matches early-scale needs and keeps PreSpawn simple: `LevelChunk` (base) then overlay `UpdateBlock`s. After spawn, `ChunkStreamSystem` fills the player's view as they move (still flat + overlays).

## Config and identity

- Operational config: `zenith.yml` next to the executable (`AppContext.BaseDirectory`), not cwd or env soup.
- Auth chain signature verification is a **YAML gate** for public exposure — LAN defaults can be softer with a boot warning.

## Frozen infrastructure

Do **not** add for its own sake: Scheduler, Actor model, full ECS, Job system, Service Locator, Runtime Manager, VisibilitySystem, DI container, or plugin API. Introduce a layer only when a real feature hits a wall the current design cannot absorb.

**Future extension form (ADR §21):** when external extensibility opens, first surface is `EventBus.Subscribe<T>` — not public `GameLoop.Register` and not hooks on `Protocol.Send*`.

Fan-out to all online players is acceptable at this stage when pose is **dirty** (ADR §44); Absolute/UpdateBlock tick egress batches per peer into one GamePacket. Visibility culling is a later product need, not an architectural prerequisite.

## Related

- [Decision history](decisions.md) — how we arrived here
- [Comparison](comparison.md) — how this differs from PocketMine-class stacks
- [`libs/leveldb/README.md`](../libs/leveldb/README.md) — KV internals and backlog

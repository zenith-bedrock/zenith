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
Gameplay → IClientChannel (ports) ⇢ Protocol → Packets → (compression) → RakNet
```

Gameplay never names a session, protocol class or packet. It talks to a client through the ports in `Gameplay/Ports/ClientPorts.cs`
(`IClientChannel` and its `IEntityChannel`, `IInventoryChannel`, `IWorldChannel`, `IChatChannel`, `IUiChannel`): `player.Client.Entity.SendAddMob(MobKind.Zombie, ...)`.
The ports belong to gameplay; the `*Protocol` classes implement them (dependency inversion), and `NetworkSession` is the `IClientChannel`.
`ArchitectureBoundaryTests.Gameplay_reaches_the_client_only_through_ports_not_session_or_protocol` enforces it on the compiled IL
(no `Zenith.Session` or `Zenith.Protocol` reference from any `Zenith.Gameplay` type), so it cannot rot.

What follows from that rule:

- Anything that is really transport orchestration lives in the Session layer, not in Gameplay: `ChunkStreamSystem` (spawn-ready
  barrier, wire order channels, handler transitions) and `PeerRelay` (skin and emote relays that read the session's profile).
- Gameplay gets what it needs from its owner, not from the session: `player.Level` (the world), `player.ItemPalette`,
  `World.Logger`, and constructor-injected services such as `BlockEditSystem`'s `ItemDurability`. `player.Session.Context.*` is gone.
- Port methods say what happened ("this mob appeared", "this container opened"), not how the wire says it: one `SendAddMob(MobKind, ...)`
  replaces eleven per-species wrappers, and `SendMobEquipment(id, inventory, slot)` takes the inventory, not a wire item.

Not done (yet): `Player.Client` returns the real session, so a test still needs the session fixture; a recording fake would need an injection
point on `Player`. The ports also still mirror protocol modules one to one; replacing them with events plus one central replication system
is the next step if more decoupling is wanted.

### Inbound (gameplay-affecting)

```
RakNet thread
  → Handler
  → pending intent (e.g. movement / place)
  → GameLoop Tick
  → System (MovementSystem, BlockDigSystem, BlockEditSystem, …)
  → Protocol → RakNet
```

The network thread **must not** mutate authoritative gameplay state (final position, inventory, world). It publishes input; the gameplay owner validates and applies it. Async I/O follows the same rule: it returns a result to the owner, which revalidates it before applying it.

## GameLoop

- Target **20 TPS**; advances `GameClock`; invokes `IGameSystem` in **registration order**.
- A system exception stops the authoritative loop and starts coordinated server shutdown. Continuing after a partially applied gameplay transition would hide corruption.
- **Single-threaded** until a concrete feature forces parallelism — no Scheduler / Actor / ECS “for cleanliness.”

Gameplay authority normally has one writer: the `GameLoop`. Networking, storage, compression and logging may run concurrently, but they do not acquire authority over `Player`, `World`, inventory or equivalent gameplay state. Keep synchronization at handoff boundaries; do not hold a lock across I/O, `await`, protocol sends or callbacks.

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

Do **not** add for its own sake: Scheduler, Actor model, Job system, Service Locator, Runtime Manager, VisibilitySystem, DI container, or plugin API. Introduce a layer only when a real feature hits a wall the current design cannot absorb.

**ECS is no longer universally frozen.** Since Phase XXI (ADR §106), expanded in Phase XXII (ADR §107), a real ECS exists in `src/zenith/Ecs/`, authoritative for six species — Zombie/Minecart/Projectile/Cow/Skeleton/Spider — see [`ecs.md`](ecs.md) for what exists and [`decisions.md` §106–§107](decisions.md) for why. Everything else — Player, sessions, inventory, storage, packets, transport, and the 6 remaining `IDamageableActor` species (Creeper, Enderman, Bat, Villager, Golem, Fish) — stays under the original freeze: no ECS/generalized runtime there without a concrete feature forcing it, same bar as every other layer in this list.

**Future extension form (ADR §21):** when external extensibility opens, first surface is `EventBus.Subscribe<T>` — not public `GameLoop.Register` and not hooks on `Protocol.Send*`.

Fan-out to all online players is acceptable at this stage when pose is **dirty** (ADR §44); Absolute/UpdateBlock tick egress batches per peer into one GamePacket. Visibility culling is a later product need, not an architectural prerequisite.

## Related

- [Decision history](decisions.md) — how we arrived here
- [ECS runtime](ecs.md) — the Phase XXI ECS design and contracts
- [Entity catalog](entities.md) — current runtime/entity capability catalog
- [Comparison](comparison.md) — how this differs from PocketMine-class stacks
- [`libs/leveldb/README.md`](../libs/leveldb/README.md) — KV internals and backlog

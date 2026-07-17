# Zenith post-impl audit — detailed checklist

Use with [SKILL.md](SKILL.md). Tick mentally while reading the diff; only report failures and notable passes.

## 1. Diff hygiene

- [ ] Changes match the user’s stated task (no drive-by refactors)
- [ ] No `bin/`, `obj/`, `worlds/`, `BenchmarkDotNet.Artifacts/`, `tmp-*`, credentials
- [ ] New files land in the correct role folder ([AGENTS.md](../../../AGENTS.md))
- [ ] No new top-level abstraction folder without ADR (`docs/decisions.md`)

## 2. Layering matrix

| If the file is under… | It may… | It must not… |
|-----------------------|---------|--------------|
| `Packets/` | Encode/Decode, DTOs, `ProtocolInfo` | Reference Player, World, Server, Gameplay |
| `Protocol/` | Build packets + send via session | Invent visibility/chunk policy or mutate inventory/world |
| `Session/` (+ handlers) | Decode, validate, `Submit*` intents, same-session UI ack | Mutate World/Inventory/final pose; fan-out to peers for chat/blocks |
| `Gameplay/` (+ Systems) | Decide on tick, mutate domain, call Protocol | `using` DataPacket / ProtocolInfo / BinaryStream |
| `World/` / `Player/` | Domain state | Wire formats |
| `Geometry/` | Pure spatial math | Minecraft hitbox literals (those stay World/EntityHitboxes) |
| `libs/nbt`, `libs/leveldb`, `src/raknet` | Leaf format/transport | Reference `zenith` project |

## 3. Intent / GameLoop

- [ ] Discrete actions (place, break, chat, ISR) use **bounded FIFO**, not overwrite-latest
- [ ] Movement (and similar continuous state) may overwrite-latest
- [ ] Systems drain on GameLoop; receive thread does not apply final gameplay state
- [ ] Cap overflow behavior is defined (drop / reject / log) — no silent unbounded growth if you touched queues

## 4. Persistence

- [ ] Hot path does not `.Wait` / `GetResult` on LevelDB Put
- [ ] LevelDB open failure is not silently replaced with InMemory
- [ ] Inventory/chest persist still happens on the paths this change touches (quit/shutdown) if relevant

## 5. Multiplayer / fan-out

- [ ] Peer replication (chat, UpdateBlock, TakeItemActor, skin, crack) originates from GameLoop or an approved Session helper with clear ADR (e.g. skin relay) — not ad-hoc handler loops
- [ ] Joiner catch-up considered if you added world-visible state (overlays, floor drops)

## 6. Freeze list (instant Blocker without ADR)

Scheduler · Actor model · ECS · Job system · Service Locator · Runtime Manager · VisibilitySystem · DI container · plugin API · `/` command **framework** (single `/gamemode` §52 is allowed) · revived `Network/`

## 7. DX / docs

- [ ] Behavior change → short note in `docs/decisions.md` if it alters an existing ADR theme
- [ ] Config via `zenith.yml` only (no new env var matrix)
- [ ] Public mental model still “decide → transmit → serialize”
- [ ] Prefer leaf tests over “works in Bedrock client”

## 8. Regression prompts (ask yourself)

| Touched area | Ask |
|--------------|-----|
| Packets Encode | Packet Id prefix? Correct item writer (Wrapper vs Descriptor vs ItemStack)? |
| AuthInput / pose | Domain Y = feet? Eye conversion only at wire boundary? |
| Floor drops / pickup | AABB + delay semantics? Republish must not reset delay? |
| Inventory / ISR | Snapshot rollback on failure? |
| Break / dig | Dig auth on intent, not live target only? |
| RakNet | Locked send on retransmit paths? Fragment/DoS caps intact? |

## 9. Test gap scoring

| Diff contains… | Expect tests in… |
|----------------|------------------|
| NBT / endian | `nbt.Tests` |
| LevelDB API / WAL | `leveldb.Tests` |
| Frames / reliability / split | `raknet.Tests` |
| Intents, systems, palettes, packet shapes, AABB | `zenith.Tests` |
| Docs-only / comments | none |

Missing tests for wire shape or intent contract → **Nit** (or **Blocker** if the change is load-bearing and untested).

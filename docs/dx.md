# Developer experience (DX)

What it feels like to work in Zenith — today and as the tree matures.

## What you get today

### 1. A readable mental model

One sentence: decide → transmit → serialize → send.

That maps to folders and types. New contributors can ask “does this class **decide**?” and get an unambiguous answer. If it decides **and** serializes, the PR is wrong — not a style preference, an architectural rule.

### 2. Leaf libraries you can open in isolation

| Project | Use without the full server |
|---------|------------------------------|
| `raknet` | Reliable UDP + `BinaryStream` |
| `nbt` (`Zenith.Nbt`) | LE / Network / BigEndian NBT round-trips |
| `leveldb` (`Zenith.LevelDB`) | Put/Get/Delete/WriteBatch/Iterator for Zenith keys |

`dotnet test` on `nbt.Tests` / `leveldb.Tests` / `zenith.Tests` keeps format bugs out of “boot the Bedrock client” loops.

### 3. Intent-first handlers

Inbound code validates and **queues**; systems apply on tick. That means:

- Reproducing “player placed block” does not require guessing thread races with RakNet
- Rate limits and bounds checks live next to decode, not deep in world mutation

### 4. Config that matches how you run binaries

`zenith.yml` next to the executable — same story in Visual Studio, `dotnet run`, or a Windows service. No hidden `ZENITH_*` matrix.

### 5. License aimed at builders

LGPL-3.0: share improvements to the library; build applications on top with a clear story. See [`LICENSE`](../LICENSE).

### 6. Docs that admit trade-offs

[`ARCHITECTURE.md`](../ARCHITECTURE.md), this `docs/` tree, and [`leveldb/README.md`](../leveldb/README.md) document **non-goals** (unbounded overlays, single-table flush rewrite, no plugins yet). Surprises are worse DX than incomplete features.

## What we deliberately do **not** ship as DX yet

| Temptation | Why wait |
|------------|----------|
| Plugin API | Wrong extension surface early becomes forever |
| DI container | Hides dependency direction; architecture relies on it being visible |
| Command framework | `/` commands deferred until chat/identity semantics settle |
| “God” event framework | EventBus exists for login/quit; domain consumers wait for need |

Good DX is saying **no** until the yes is cheap to maintain.

## Workflow suggestions

```text
1. Read docs/architecture.md + ARCHITECTURE.md
2. Touch the smallest leaf (nbt/leveldb/raknet) when possible
3. Add a unit test before a Bedrock client smoke when the change is format/protocol shape
4. Keep gameplay free of DataPacket / BinaryStream
5. If you need a new abstraction (Factory, ECS, Scheduler): justify against freeze list
```

Manual smoke expectations (clients A/B, terrain hashes, chat, place/break) live in [`ARCHITECTURE.md`](../ARCHITECTURE.md).

## IDE / tooling

- **.NET 10** solution (`zenith.sln`) with focused test projects
- Nullable + modern C# patterns (`ref struct` streams, `ValueTask` storage)
- YAML for ops config; future player-facing strings planned as TOML (`lang/*.toml`) — not mixed into `zenith.yml`

## Extending later (shape, not API yet)

When extension points open, prefer:

```
Gameplay domains  →  stable intents / events
                 ↘ Protocol (already decided sends)
```

Avoid:

```
Plugin → raw packet decode → mutate World on RakNet thread
```

That antipattern is how Bedrock stacks become un-upgradable across protocol bumps.

## Related

- [Comparison](comparison.md) — when Zenith DX wins vs plugin ecosystems  
- [Why Zenith](why-zenith.md) — long-term bet

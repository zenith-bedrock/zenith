# Decision history

This log reconstructs **why** Zenith looks the way it does, using the git timeline (Dec 2024 → Jul 2026) and the living rules in [`ARCHITECTURE.md`](../ARCHITECTURE.md). Newest themes first within each era.

## Timeline (milestones)

| Period | Commits (indicative) | Theme |
|--------|----------------------|--------|
| Dec 2024 | `5064e74` → `881183b` | RakNet foundation: BinaryStream, FrameSet, login / StartGame plumbing |
| Jul 2026 | `1dd0501` → `ea6b6f1` | Session extraction, protocol **1.26.33** alignment, compression |
| Jul 2026 | `d63e7b2`, `548b289` | `BinaryStream` as **ref struct**; Packets vs Protocol split; GameLoop |
| Jul 2026 | `496cbb5` → `33ba520` | Visibility, chat, world read path, rate limits |
| Jul 2026 | `6cdc494` → `78950da` | `zenith.yml`, flat terrain, inventory/blocks |
| Jul 2026 | `2b1eed9` (+ follow-ups) | Owned **Zenith.Nbt** + **Zenith.LevelDB**, block palette, hash flag fix |

## The decisions

Every decision is its own file under [`adr/`](adr/README.md), numbered from §1; that index is the way in and explains how to add one. Code and docs cite a decision as `ADR §N`. The ones that explain the shape of the whole project (own the stack from UDP up, `BinaryStream` as a `ref struct`, the Packets/Protocol/Handlers/Gameplay split, GameLoop over Actor/ECS, overlay persistence, owned NBT and LevelDB) are §1 to §6.

## Explicit non-goals (so far)

Recorded so we don't “accidentally” implement them. This list is the one part of the log that is a living document rather than a record, so it stays here:

- Plugin API / DI container
- **Replacing** ZLDB with full Mojang LSM on the default path (Mojang open/import → §61 seam + converter instead)
- Multi-level LSM compaction **inside** `Zenith.LevelDB` / PInvoke RocksDB as the product store (unless RAM/streaming need is proven — still not “become BDS”)
- Full creative catalog / `block_state_b64` decode (short CreativeContent list shipped in §31)
- Generic WorldEntity/ECS before ADR §99's evidence gate; broad mob AI before actor foundation (drop-item wire shipped without a generic entity layer — §32). **Superseded for a deliberately small slice by §106, expanded by §107** (Zombie/Minecart/Projectile/Cow/Skeleton/Spider, Phase XXI–XXII) **and again by §132** (Fish/Creeper/Enderman/Golem) — ten species total. The remaining two species (Bat, Villager), and every other domain in this list, are still governed by this non-goal unchanged.
- Plugin-facing command registration/discovery and public permission hooks; the protocol-independent first slice is allowed by ADR §100
- Protocol bump solely to chase client log version numbers when login already completes
- Actor/EventHandler frameworks copied from other engines
- A *generated* packet-dispatch table (derived from `[GamePacket]` or any other codegen source) replacing the per-handler `switch` statements (§76 — wire codegen stops at `Encode`/`Decode`/`Id`). **Narrowed by §113**: a *hand-written* registry map for one handler with a demonstrated same-codebase precedent (`CommandCatalog`) is not covered by this non-goal — `InGameSessionHandler` already uses one; the other three session handlers still use a plain `switch` and this non-goal still applies to them until similarly justified.

## Zenith v0.0.1-alpha

**Not production-ready.** LAN / private feedback only.

### Includes (LAN spine)

- Login → flat world + overlay place/break
- Inventory 36 slots + ISR rearrange
- Held-item peer sync
- LevelDB Zenith keys `c:` / `ov:` under `worlds/<name>/`
- Chat + player visibility
- `dotnet test zenith.sln` green at tag time

### Non-goals (explicitly out of this tag)

- Chests / container inventory (§19 sketch)
- CreativeContent, Mojang vanilla worlds
- Plugins / DI / `/` commands
- Inventory or position persistence across reconnect (`players/` not on disk yet)
- AuthInput block path
- Actor / domain EventHandlers
- Public production with `auth.require-chain-signatures: false`

### Ops traps

| Trap | Reality |
|------|---------|
| `world.path` empty | **InMemory** — world wiped on restart, **no warning** |
| Docker volumes | Mount `./data/zenith.yml:/app/zenith.yml` **and** `./data/worlds:/app/worlds`. Do **not** mount `./data:/app` (overwrites the DLL). |
| Layout | LevelDB at `{world.path}/worlds/{world.name}/` (compose sample: `/app/worlds/world`) |
| `players/` | **Not shipped** — session RAM only |
| Auth | Default sample leaves chain signatures off for LAN; enable before public exposure |

### Run (Docker)

```bash
mkdir -p data/worlds
# ensure data/zenith.yml exists (repo sample under data/zenith.yml)
docker compose up --build
```

### Product vs protocol version

- Product: `ServerIdentity.ProductVersion` / assembly informational version (`0.0.1-alpha`)
- Wire: `ProtocolVersion` / `VersionName` (Bedrock client) — unchanged by this release tag

## Zenith v0.0.1-alpha

**Not production-ready.** LAN / private feedback only.

### Includes (LAN spine)

- Login → flat world + overlay place/break; chat + player visibility
- Inventory 36 slots + ISR rearrange; held-item peer sync
- LevelDB Zenith keys `c:` / `ov:` (and `ct:` / `inv:`) under `worlds/<name>/`
- Chests (§28) + chest facing (§46), 2×2 crafting (§35), Creative from config (§31 / §38)
- Inventory / chest persist across graceful restart (§39 / §41)
- Server-authoritative break timing + dig crack (self + peers, §27 / §42)
- Void soft-rescue with local MovePlayer Teleport (§40 / §41)
- MOTD online count + session hygiene (§47); ordered RakNet fragment reassembly
- `dotnet test zenith.sln` green at tag time

### Non-goals (explicitly out of this tag)

- Mojang vanilla worlds; `players/` volume
- Plugins / DI / `/` commands / `/gamemode`
- Drop-entity wire / WorldEntity; death–Respawn handshake
- Tool speed / efficiency; block gravity (sand/gravel); biomes / noise; hunger tick
- Double-chest 54 UI (facing-only is shipped)
- Public production with `auth.require-chain-signatures: false`
- Actor / domain EventHandler frameworks

### Ops traps

| Trap | Reality |
|------|---------|
| `world.path` empty | **InMemory** — world wiped on restart, **no warning** |
| Relative `world.path` | Resolved under the DLL directory (`AppContext.BaseDirectory`), **not** the shell cwd. Prefer `.` or an absolute root (`/app`). |
| `world.path: ./worlds` | Wrong — `worlds/<name>/` is appended under the root; you get `…/worlds/worlds/<name>`. |
| Docker volumes | Mount `./deploy/zenith.yml:/app/zenith.yml` **and** `./deploy/worlds:/app/worlds`. Do **not** mount a host folder over `/app` (overwrites the DLL). Redeploy without a durable `/app/worlds` volume = wipe. |
| Layout | LevelDB at `{world.path}/worlds/{world.name}/` (compose sample: `/app/worlds/world`) |
| Stop / redeploy | Prefer SIGTERM (compose/Dokploy stop) so `FlushAsync` runs; `kill -9` can lose recent bag/chest Puts |
| `players/` | **Not shipped** — no Mojang-style playerdata volume (bag/chest live under world LevelDB keys) |
| Auth | Default sample leaves chain signatures off for LAN; enable before public exposure |

### Run (Docker)

```bash
mkdir -p deploy/worlds
# ensure deploy/zenith.yml exists (repo sample)
docker compose up --build
```

### Product vs protocol version

- Product: `ServerIdentity.ProductVersion` / assembly informational version (`0.0.1-alpha`)
- Wire: `ProtocolVersion` / `VersionName` (Bedrock client) — unchanged by this release tag

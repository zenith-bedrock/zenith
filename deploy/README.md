# Deploy

Host-side packaging — **not** used by `dotnet run`.

## Contract (SSOT)

**Who packages ≠ who hosts ≠ who configures.**

| Piece | Role |
|-------|------|
| [`image/`](image/) | **Product image** — one Dockerfile, binary `/opt/zenith`, entrypoint |
| `$ZENITH_DATA` | Config + worlds + `server.guid` (default `/data`) |
| [`zenith.yml`](zenith.yml) | Single default template (`world.path` empty → use `ZENITH_DATA`) |
| [`compose/`](compose/) | **Compose SSOT** — generic + Dokploy adapter |
| [`pterodactyl/`](pterodactyl/) | Egg adapter only (same image) |

Root `Dockerfile` / `docker-compose.yml` are thin entrypoints (include / mirror) so `docker build .` and `docker compose up` keep working — edit under `deploy/`, not copies at root.

```text
/opt/zenith/zenith.dll     ← image (read-only)
$ZENITH_DATA/zenith.yml    ← ops config
$ZENITH_DATA/worlds/<name>/ ← LevelDB
$ZENITH_DATA/server.guid   ← stable RakNet identity
```

Env matrix (containers only): **`ZENITH_DATA`** + panel **`SERVER_PORT`** (Pterodactyl). No other `ZENITH_*`.

Build:

```bash
docker build -f deploy/image/Dockerfile -t ghcr.io/zenith-bedrock/zenith:latest .
# Dokploy / `docker build .` uses the root Dockerfile (kept in sync with deploy/image/).
```

## Platforms

| Host | Adapter | `ZENITH_DATA` |
|------|---------|---------------|
| Compose / local | [`compose/docker-compose.yml`](compose/docker-compose.yml) (root includes it) | `/data` |
| Dokploy | [`compose/docker-compose.dokploy.yml`](compose/docker-compose.dokploy.yml) — [`compose/dokploy.md`](compose/dokploy.md) | `/data` |
| Pterodactyl / Wings | [`pterodactyl/`](pterodactyl/) | `/home/container` |

Adding another host = new adapter under `deploy/` — **zero** new product Dockerfile.

## Quick start (Compose)

From repo root:

```bash
docker compose up --build
```

Named volume `zenith-data` → `/data`. Config is read **only at boot** — restart after edits.

Dev bind-mount:

```bash
cp deploy/compose/override.example.yml docker-compose.override.yml
mkdir -p deploy/data
docker compose up --build
# edit deploy/data/zenith.yml → docker compose restart zenith
```

## Local / IDE (`dotnet run`)

Config next to the DLL (`AppContext.BaseDirectory`). Empty `world.path` without `ZENITH_DATA` = InMemory. Schema (IDE only): [`schemas/zenith.schema.json`](../schemas/zenith.schema.json).

## References

- ADR §20 (layout + unified image), §68 (Pterodactyl hooks)
- [`docs/dx.md`](../docs/dx.md)

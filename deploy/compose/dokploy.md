# Dokploy

Same **product image** as Compose ([`../image/`](../image/)). Set `ZENITH_DATA=/data` and mount one volume on `/data`.

## Provider

| Field | Value |
|-------|-------|
| Source | Git → this repo |
| Branch | `develop` (or your pin) |
| **Compose Path** | **`./deploy/compose/docker-compose.dokploy.yml`** |

Do **not** use `./docker-compose.yml` for File Mount — that file is the generic include (no `../files/`) so local/CI stay safe.

SSOT lives under [`deploy/compose/`](.) — root `docker-compose.yml` is only a thin `include` for `docker compose up`.

## Steps

1. **Build** via the Dokploy compose (`deploy/image/Dockerfile`).
2. **Expose** UDP `19132`.
3. **Environment:** `ZENITH_DATA=/data` (image default; set in UI if the stack strips env).
4. **Volume:** named volume `zenith-data` → `/data` (already in compose).
5. **File Mount (config UI):** Advanced → Mounts → **File**
   - **File Path:** `zenith.yml` (stored under Dokploy `…/files/`)
   - **Mount Path:** often shows `/` in Compose — **ignore**; the compose bind is what matters
   - **Content:** paste your YAML (see template below)
6. **Save** → **Redeploy**.
7. **Worlds** persist under `/data/worlds/` on the same volume.

The Dokploy compose already contains:

```yaml
volumes:
  - zenith-data:/data
  - ../../../files/zenith.yml:/data/zenith.yml:ro
```

(Path is relative to `deploy/compose/` → Dokploy `files/` sibling of `code/`.)

## Sample File Mount content

```yaml
server:
  port: 19132
  motd: Zenith Bedrock
  sub-motd: Test
  max-players: 20
  max-players-per-ip: 3
  gamemode: Survival
world:
  name: world
  path: ""
  spawn-chunk-radius: 4
  spawn-ready-radius: 2
  terrain: noise
  seed: 42
auth:
  accept:
    - xbox
    - self-signed
    - offline
chat:
  max-length: 512
  rate-capacity: 8
  rate-refill-per-second: 4
network:
  compression-threshold: 256
log:
  server: info
  raknet: warn
```

`world.path` empty + `ZENITH_DATA=/data` → LevelDB at `/data/worlds/world`.

## Boot checks

```text
world.terrain=noise seed=42
World storage: LevelDbChunkStorage (... via=ZENITH_DATA)
Server ready.
```

## Common failures

| Symptom | Cause | Fix |
|---------|--------|-----|
| Still `terrain=flat` / old config | Wrong Compose Path or File Mount not wired | Compose Path `./deploy/compose/docker-compose.dokploy.yml`; File Mount `zenith.yml`; redeploy |
| Boot: `zenith.yml is a directory` | Host path missing; Docker created a directory | Ensure File Mount content exists before redeploy |
| Config edits ignored | Edited `/opt/zenith/...` or generic compose | Use Dokploy compose + File Mount → `/data/zenith.yml:ro` |
| “Persist broken” after redeploy | Volume not reattached | Named volume on `/data` |
| Empty world after path change | Old LevelDB path (ADR §20) | Boot log: `via=ZENITH_DATA` |
| Build: `GID '1000' already exists` | Stale image | Rebuild `0abdea6+` |

**Do not mount over `/opt/zenith`** — that replaces the published DLL.

## Local compose (not Dokploy)

```bash
docker compose up --build
# or explicitly:
docker compose -f deploy/compose/docker-compose.yml up --build
```

No `files/` bind — edit via [`override.example.yml`](override.example.yml) or `docker compose exec`.

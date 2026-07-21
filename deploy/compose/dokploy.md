# Dokploy

Same **product image** as Compose ([`../image/`](../image/)). Set `ZENITH_DATA=/data` and mount one volume on `/data`.

## Provider

| Field | Value |
|-------|-------|
| Source | Git → this repo |
| Branch | `develop` (or your pin) |
| **Compose Path** | **`./docker-compose.dokploy.yml`** |

Do **not** use `./docker-compose.yml` if you want File Mount for `zenith.yml` — the generic compose stays free of `../files/` so local/CI deploys are not broken.

## Steps

1. **Build** from this repo (`deploy/image/Dockerfile` via the Dokploy compose).
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
  - ../files/zenith.yml:/data/zenith.yml:ro
```

(`../files/` is relative to the Git `code/` checkout — Dokploy layout.)

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
| Still `terrain=flat` / old config | Compose Path still `./docker-compose.yml` or File Mount not wired | Set Compose Path to `./docker-compose.dokploy.yml`; create File Mount `zenith.yml`; redeploy |
| Boot: `zenith.yml is a directory` | Host path for a file mount did not exist; Docker created a directory | Fix File Path name; ensure File Mount content exists before redeploy |
| Config edits ignored | Edited `/opt/zenith/...` or wrong compose | File Mount + Dokploy compose bind to `/data/zenith.yml:ro` |
| “Persist broken” after redeploy | New container without the same volume | Reattach named volume on `/data` |
| Empty world after path change | Old LevelDB at wrong path (ADR §20) | Boot log: `via=ZENITH_DATA` |
| Build: `GID '1000' already exists` | Stale image | Pull/rebuild `0abdea6+` |

**Do not mount over `/opt/zenith`** — that replaces the published DLL.

## Local compose (not Dokploy)

```bash
docker compose -f docker-compose.yml up --build
```

No `../files/` — edit via bind override ([`override.example.yml`](override.example.yml)) or `docker compose exec`.

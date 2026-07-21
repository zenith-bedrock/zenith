# Deploy (Docker / Dokploy)

Host-side layout for containers — **not** used by `dotnet run`.

## One volume on `/data` (recommended)

PocketMine, Endstone, and most Bedrock images use a **single data directory**, not a config file mount next to the binary. Zenith follows the same pattern:

| Container path | Purpose |
|----------------|---------|
| `/data/zenith.yml` | Operational config (created on first boot from image default) |
| `/data/worlds/<name>/` | LevelDB world (`world.path: /data`, `world.name: world`) |

**Do not mount over `/app`** — that replaces the published DLL and the server will not start.

### docker compose

```bash
docker compose up --build
```

Uses named volume `zenith-data` → `/data`. Edit config:

```bash
docker compose exec zenith sh -c 'cat /data/zenith.yml'
# edit on host with bind mount (dev):
cp deploy/docker-compose.override.example.yml docker-compose.override.yml
mkdir -p deploy/data
docker compose up --build
# then edit deploy/data/zenith.yml and restart the container
```

**Config is read only at boot.** After changing `zenith.yml`, restart the container (`docker compose restart zenith` or Dokploy redeploy with the same volume attached).

### Dokploy

1. **Build** from this repo (Dockerfile at root).
2. **Expose** UDP `19132`.
3. **Environment:** `ZENITH_DATA=/data` (already set in the image; repeat in UI if your stack strips env).
4. **Volume:** mount a persistent volume at **`/data`** (not `/app`, not `/app/zenith.yml`).
5. **First deploy:** entrypoint copies `deploy/zenith.yml` → `/data/zenith.yml`.
6. **Change config:** open `/data/zenith.yml` inside the volume (Dokploy file manager, `docker exec`, or SFTP sidecar) → **restart** the app.
7. **Worlds** persist automatically under `/data/worlds/` on the same volume.

#### Common Dokploy failures

| Symptom | Cause | Fix |
|---------|--------|-----|
| Boot: `zenith.yml is a directory` | Host path for a file mount did not exist; Docker created a directory | Remove the bad mount; use volume on `/data` only |
| Config edits ignored | Edited `/app/zenith.yml` or a path not mounted | Edit `/data/zenith.yml`; confirm volume is attached |
| “Persist broken” after redeploy | New container without the same volume | Reattach the named volume on `/data` |
| Empty world after path change | Old LevelDB at wrong path (ADR §20) | Check boot logs for `World storage: LevelDbChunkStorage (...)` |

### Image-only run (no compose)

```bash
docker run -d --name zenith -p 19132:19132/udp -v zenith-data:/data ghcr.io/you/zenith:latest
docker exec -it zenith sh
# vi /data/zenith.yml  →  docker restart zenith
```

## Local / IDE (`dotnet run`)

Config and optional world live next to the built DLL (`AppContext.BaseDirectory`). Game palettes are embedded at build time under `src/zenith/data/`.

Schema (IDE only): [`schemas/zenith.schema.json`](../schemas/zenith.schema.json). Runtime validation: `ServerConfig.Validate()`.

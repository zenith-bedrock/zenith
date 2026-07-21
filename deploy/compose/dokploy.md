# Dokploy

Same **product image** as Compose ([`../image/`](../image/)). Set `ZENITH_DATA=/data` and mount one volume on `/data`.

## Steps

1. **Build** from this repo (root `Dockerfile` or `deploy/image/Dockerfile`).
2. **Expose** UDP `19132`.
3. **Environment:** `ZENITH_DATA=/data` (image default; set in UI if the stack strips env).
4. **Volume:** persistent volume at **`/data`** (not `/opt/zenith`).
5. **First deploy:** entrypoint seeds `/data/zenith.yml` from the image default (`world.path` empty → LevelDB under `/data/worlds/`).
6. **Change config (UI):** Advanced → Mounts → File Mount — **File Path:** `zenith.yml`. Add to compose:

   ```yaml
   volumes:
     - zenith-data:/data
     - ../files/zenith.yml:/data/zenith.yml:ro
   ```

   Redeploy + restart.
7. **Change config (volume):** edit `/data/zenith.yml` inside the volume → **restart**.
8. **Worlds** persist under `/data/worlds/` on the same volume.

## Common failures

| Symptom | Cause | Fix |
|---------|--------|-----|
| Boot: `zenith.yml is a directory` | Host path for a file mount did not exist; Docker created a directory | Remove the bad mount; volume on `/data` only |
| Config edits ignored | Edited `/opt/zenith/...` or File Mount missing | File Mount `zenith.yml` → `/data/zenith.yml:ro`; redeploy |
| “Persist broken” after redeploy | New container without the same volume | Reattach named volume on `/data` |
| Empty world after path change | Old LevelDB at wrong path (ADR §20) | Boot log: `World storage: LevelDbChunkStorage (... via=ZENITH_DATA)` |
| GUID under `/opt` or `/app` | Stale image without `ResolvePersistentRoot` | Rebuild/redeploy current image |

**Do not mount over `/opt/zenith`** — that replaces the published DLL.

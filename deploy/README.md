# Deploy sample (Docker only)

Host-side mounts for `docker compose` — **not** used by `dotnet run`.

| Path | Role |
|------|------|
| `deploy/zenith.yml` | Sample config (`world.path: /app`) — **tracked in git** (needed for Dokploy/clone mounts) |
| `deploy/worlds/` | Bind mount → container `/app/worlds` (gitignored contents; Docker creates the dir) |

If `deploy/zenith.yml` is missing on the host, Docker turns the file mount into a **directory** and boot fails with access denied / “is a directory”.

**Dokploy / redeploy:** the worlds volume must survive the new container. Restart/stop (SIGTERM) flushes LevelDB; a rebuild that drops an ephemeral `/app/worlds` looks like “persist broken” even when graceful flush works.

Local / IDE: config + world live under `src/zenith/bin/.../` (`AppContext.BaseDirectory`).  
Game palettes embedded at build time: `src/zenith/data/`.

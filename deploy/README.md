# Deploy sample (Docker only)

Host-side mounts for `docker compose` — **not** used by `dotnet run`.

| Path | Role |
|------|------|
| `deploy/zenith.yml` | Sample config (`world.path: /app`) |
| `deploy/worlds/` | Bind mount → container `/app/worlds` (gitignored contents) |

Local / IDE: config + world live under `src/zenith/bin/.../` (`AppContext.BaseDirectory`).  
Game palettes embedded at build time: `src/zenith/data/`.

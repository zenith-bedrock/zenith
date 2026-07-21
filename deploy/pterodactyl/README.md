# Pterodactyl / Wings

Adapter only (ADR §68) — **same product image** as Compose ([`../image/`](../image/)). No second Dockerfile.

## Architecture

```text
Panel  →  Wings  →  container
                         ├── ZENITH_DATA=/home/container
                         ├── zenith.yml + worlds/   (Wings volume)
                         └── binary: /opt/zenith/zenith.dll (image)
```

## 1. Build / pull the product image

```bash
docker build -f deploy/image/Dockerfile -t ghcr.io/zenith-bedrock/zenith:latest .
docker push ghcr.io/zenith-bedrock/zenith:latest   # or private registry on the node
```

## 2. Import the egg

1. Download [`egg-zenith.json`](egg-zenith.json)
2. Panel → **Admin → Nests → Import Egg**
3. Create a server: **512MB+ RAM**, allocation **UDP** (Bedrock)

## 3. First start

- Install script seeds [`deploy/zenith.yml`](../zenith.yml) if missing
- Egg yaml parser sets `server.port` from the allocation
- **`SERVER_PORT`** also overrides yaml at boot (backup)
- Boot log **`Server ready.`** marks the server as started
- Stop: panel stop → SIGTERM → LevelDB flush

## 4. Editing config

File Manager / SFTP on `/home/container/zenith.yml` → **restart**.

| Key | Example |
|-----|---------|
| `server.motd` | Server list title |
| `world.terrain` | `flat` or `noise` |
| `world.seed` | Integer for noise mode |
| `auth.accept` | Public: `[xbox]` only |
| `world.path` | Leave empty (uses `ZENITH_DATA`) |

## Panel hooks (product code)

| Mechanism | Purpose |
|-----------|---------|
| `ZENITH_DATA=/home/container` | Config + worlds path |
| `SERVER_PORT` | Allocation → `server.port` |
| `Server ready.` | Egg `startup.done` |
| `server.guid` | Under persistent root |
| SIGTERM | Graceful shutdown (ADR §41) |

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Stuck on *Starting* | Logs must show `Server ready.` — UDP bind / port conflict |
| Wrong port | Primary allocation UDP; egg parser updates `zenith.yml` |
| Image pull failed | Build/push `deploy/image/Dockerfile` on the node |
| Empty world | Check `via=ZENITH_DATA` in boot log |

## References

- Endstone egg pattern: [EndstoneMC/pterodactyl](https://github.com/EndstoneMC/pterodactyl)
- [Creating a custom egg](https://pterodactyl.io/community/config/eggs/creating_a_custom_egg.html)
- Deploy contract: [`../README.md`](../README.md)

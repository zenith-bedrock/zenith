# v0.0.1-alpha release gate

Tag only when this checklist is green. Checkboxes are **human smoke / compose / release proof** — do not tick from CI alone. Evidence dates below are from Jul 2026 client sessions (see chat history + [`ARCHITECTURE.md`](../ARCHITECTURE.md) § Smoke manual).

**Implementation note:** the product spine is **coded** (ADRs §17–§42 / §46–§48 + leaf tests). Sand/gravel fall physics is **not** required here (Horizon 1). Remaining Horizon 0 work is mostly **tag + release notes** — see [`roadmap.md`](roadmap.md).

## Product spine

- [x] Login → flat world + overlay place/break; chat + player visibility *(baseline 1–10)*
- [x] Inventory 36 + ISR rearrange; held-item peer sync *(11 + 12 OK)*
- [x] LevelDB at `{world.path}/worlds/{world.name}/` (`c:` / `ov:`) *(Dokploy + local `world.path`)*
- [x] Chests (§28) + 2×2 craft (§35) + Creative join from config (§31 / §38) *(S35 / S37 / S38)*
- [x] Inventory + chest persist (`inv:` / `ct:`, §39) + graceful flush on Ctrl+C (§41) *(S39 / S39b)*
- [x] SA break timing + dig crack (§27); crack visible to peers (§42); void soft-rescue MovePlayer (§40 / §41) *(S40 / S41)*
- [x] `ServerIdentity.ProductVersion` logged; wire `ProtocolVersion` / `VersionName` unchanged *(boot log `0.0.1-alpha`)*

## CI / local proofs

```bash
dotnet test zenith.sln -c Release
dotnet run -c Release --project src/zenith.Benchmarks -- -f * -j short -m --join
```

- [x] All tests green *(leaf suite green through Jul 2026; re-run once at tag time)*
- [x] Benchmarks project builds (numbers refreshed in `docs/dx.md` — hot-path suite: ZLIB / UpdateBlock / LevelChunk / inventory wire / overlay / palette)

## Docker / compose

```bash
mkdir -p deploy/worlds
# use repo sample deploy/zenith.yml (world.path: /app)
docker compose up --build
```

- [x] UDP `19132` listens *(Dokploy compose)*
- [x] LevelDB appears under `./deploy/worlds/world` (or configured name) *(Dokploy; note: redeploy without volume wiped — ops trap)*
- [x] Empty `world.path` trap documented in release notes (InMemory, no warning)

## Manual smoke (see ARCHITECTURE.md)

- [x] Baseline MP **1–12** OK *(Jul 2026). **6** was PARCIAL (AFK join miss) → fixed join-overlay catch-up (§14); **12 held peer** OK)*
- [x] **S37** Creative fly / Survival no MayFly
- [x] **S38** Creative palette → cursor / SHIFT → bag (fix CreatedOutput+Place)
- [x] **S39** / **S39b** LevelDB bag+chest persist (graceful + quit)
- [x] **S40** void → spawn snap, health 20
- [x] **S35** Survival 2×2 craft (cadeia planks→chest OK)
- [x] **S41** peer sees crack / limpa
- [x] Overlay: graceful restart keeps placed blocks *(Dokploy/local restart; literal “100 blocks” count not required)*
- [x] Crash soft check: place blocks → brief pause → hard kill (`kill -9` / `taskkill /F`) → restart → overlays/world that already hit WAL survived *(Jul 2026 — OK; bag/`inv:`/`ct:` recent still not guaranteed without `FlushAsync`)*

## Tag & publish

```bash
git tag v0.0.1-alpha
git push origin v0.0.1-alpha
```

`.github/workflows/release.yml` runs test → publish linux-x64 zip → GitHub Release with `docs/release-notes-template.md`.

**Before tag:** release notes filled; `dotnet test -c Release` green (Jul 2026).

## Explicit non-goals on the tag

Mojang vanilla worlds; `players/` volume; plugins / DI / `/` / `/gamemode`; drop-entity / WorldEntity; death–Respawn; tool speed / efficiency; block gravity (sand/gravel); biomes / noise; hunger tick; public production with `auth.accept` containing `self-signed` or `offline`.

## Ops traps (must stay in release notes)

| Trap | Reality |
|------|---------|
| `world.path` empty | **InMemory** — wiped on restart, **no warning** |
| Relative `world.path` | Under `AppContext.BaseDirectory` (DLL dir), not shell cwd |
| Auth sample | LAN default `accept` includes self-signed/offline; boot WARNING; use `[xbox]` before public exposure |
| ZLIB decompression (2 MB cap) | Oversized inflate throws; RakNet receive loop logs and drops the datagram (no explicit session disconnect yet). LAN risk low; public is still a DoS/CPU surface |
| Docker / Dokploy volume | Persist host dir → `/app/worlds` (compose sample). Redeploy **without** that volume wipes LevelDB even when flush is correct |
| Docker stop / redeploy | Process handles **SIGTERM** → `ShutdownAsync` / `FlushAsync`. Hard kill still drops in-flight Puts |

# v0.0.1-alpha release gate

Tag only when this checklist is green. Checkboxes stay **manual** at tag time — do not claim they are already green in-repo.

**Implementation note:** the product spine rows below are **coded** (ADRs §17–§42 / §46–§48 + leaf tests). Empty boxes mean “human smoke / compose / release proof still outstanding,” not “feature missing.” Remaining Horizon 0 work is proving that spine on real clients and shipping the tag — see [`roadmap.md`](roadmap.md). Sand/gravel fall physics is **not** required here (Horizon 1).

## Product spine

- [ ] Login → flat world + overlay place/break; chat + player visibility
- [ ] Inventory 36 + ISR rearrange; held-item peer sync
- [ ] LevelDB at `{world.path}/worlds/{world.name}/` (`c:` / `ov:`)
- [ ] Chests (§28) + 2×2 craft (§35) + Creative join from config (§31 / §38)
- [ ] Inventory + chest persist (`inv:` / `ct:`, §39) + graceful flush on Ctrl+C (§41)
- [ ] SA break timing + dig crack (§27); crack visible to peers (§42); void soft-rescue MovePlayer (§40 / §41)
- [ ] `ServerIdentity.ProductVersion` logged; wire `ProtocolVersion` / `VersionName` unchanged

## CI / local proofs

```bash
dotnet test zenith.sln -c Release
dotnet run -c Release --project src/zenith.Benchmarks -- -f * -j short -m --join
```

- [ ] All tests green
- [ ] Benchmarks project builds (numbers optional refresh in `docs/dx.md` — hot-path suite: ZLIB / UpdateBlock / LevelChunk / inventory wire / overlay / palette)

## Docker / compose

```bash
mkdir -p deploy/worlds
# use repo sample deploy/zenith.yml (world.path: /app)
docker compose up --build
```

- [ ] UDP `19132` listens
- [ ] LevelDB appears under `./deploy/worlds/world` (or configured name)
- [ ] Empty `world.path` trap documented in release notes (InMemory, no warning)

## Manual smoke (see ARCHITECTURE.md)

- [ ] Baseline MP 1–12
- [x] **S37** Creative fly / Survival no MayFly
- [x] **S38** Creative palette → cursor / SHIFT → bag (fix CreatedOutput+Place)
- [x] **S39** / **S39b** LevelDB bag+chest persist (graceful + quit)
- [x] **S40** void → spawn snap, health 20
- [x] **S35** Survival 2×2 craft (cadeia planks→chest OK)
- [x] **S41** peer sees crack / limpa
- [ ] Overlay: place 100 blocks → **graceful** restart → blocks intact (< 10k warn threshold)
- [ ] Crash soft check (optional): place blocks → brief pause → `kill -9` → restart → LevelDB `CURRENT` world/overlays that already hit WAL may survive; **do not** require recent `inv:`/`ct:` intact — Puts are fire-and-forget until `FlushAsync` on graceful shutdown (§39 / §41)

## Tag & publish

```bash
git tag v0.0.1-alpha
git push origin v0.0.1-alpha
```

`.github/workflows/release.yml` runs test → publish linux-x64 zip → GitHub Release with `docs/release-notes-template.md`.

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

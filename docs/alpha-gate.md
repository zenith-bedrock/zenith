# v0.0.1-alpha release gate

Tag only when this checklist is green. Checkboxes stay **manual** at tag time — do not claim they are already green in-repo.

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
- [ ] Benchmarks project builds (numbers optional refresh in `docs/dx.md`)

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

- [ ] Baseline MP 1–12 + S35 / S37 / S38 / S39 / S39b / S40 / **S41** (peer sees crack)
- [ ] Overlay: place 100 blocks → restart → blocks intact (< 10k warn threshold)
- [ ] Crash recovery: place block + chest item + inventory item → `kill -9` server → restart → world, chest, inventory intact (LevelDB CURRENT trust)

## Tag & publish

```bash
git tag v0.0.1-alpha
git push origin v0.0.1-alpha
```

`.github/workflows/release.yml` runs test → publish linux-x64 zip → GitHub Release with `docs/release-notes-template.md`.

## Explicit non-goals on the tag

Mojang vanilla worlds; `players/` volume; plugins / DI / `/` / `/gamemode`; drop-entity / WorldEntity; death–Respawn; tool speed / efficiency; biomes / noise; hunger tick; public production with `auth.require-chain-signatures: false`.

## Ops traps (must stay in release notes)

| Trap | Reality |
|------|---------|
| `world.path` empty | **InMemory** — wiped on restart, **no warning** |
| Relative `world.path` | Under `AppContext.BaseDirectory` (DLL dir), not shell cwd |
| Auth sample | Chain signatures often off for LAN; boot WARNING; enable before public exposure |
| ZLIB decompression (2 MB cap) | Malformed/zombie compressed payload rejected with disconnect; LAN risk low, public is DOS surface |

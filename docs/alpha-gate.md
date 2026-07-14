# v0.0.1-alpha release gate

Tag only when this checklist is green. Chests (§19) do **not** block alpha.

## Product spine

- [ ] Login → flat world + overlay place/break
- [ ] Inventory 36 + ISR rearrange
- [ ] Held-item peer sync
- [ ] LevelDB at `{world.path}/worlds/{world.name}/` (`c:` / `ov:`)
- [ ] Chat + visibility
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
mkdir -p data/worlds
# use repo sample data/zenith.yml (world.path: /app)
docker compose up --build
```

- [ ] UDP `19132` listens
- [ ] LevelDB appears under `./data/worlds/world` (or configured name)
- [ ] Empty `world.path` trap documented in release notes (InMemory, no warning)

## Tag & publish

```bash
git tag v0.0.1-alpha
git push origin v0.0.1-alpha
```

`.github/workflows/release.yml` runs test → publish linux-x64 zip → GitHub Release with `docs/release-notes-template.md`.

## Explicit non-goals on the tag

Chests, CreativeContent, Mojang worlds, plugins/DI/`/`, inventory reconnect persist, AuthInput blocks, Actor, production without `auth.require-chain-signatures: true`, `players/` volume.

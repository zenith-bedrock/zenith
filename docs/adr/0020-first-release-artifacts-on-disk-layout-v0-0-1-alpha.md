# ADR §20 — First release artifacts + on-disk layout (`v0.0.1-alpha`)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §20`.

**Choice:** Ship a product version separate from Bedrock wire: `ServerIdentity.ProductVersion` (e.g. `"0.0.1-alpha"`). Docker: `WORKDIR /app`; host sample under `deploy/` — mount `./deploy/zenith.yml:/app/zenith.yml` and `./deploy/worlds:/app/worlds` (not used by `dotnet run`). On-disk world = `{world.path}/worlds/{world.name}/` LevelDB (path empty ⇒ InMemory). Sample compose uses `world.path: /app`, `world.name: world`. Multi-world load stays a non-goal; folder convention only.

**Why:** No published channel existed for external feedback (Vedrock already had Docker + tag). `zenith.yml` resolves via `AppContext.BaseDirectory` next to the DLL — config cannot live solely under a PMMP-style `/data` that replaces `/app`. A separate `players/` volume was deferred until playerdata existed.

**Adendo (jul 2026 — playerdata without `players/` volume):** Pose + GameMode now persist in the world LevelDB as `pd:{uuid}` (§60), same family as `inv:` / `ct:`. A separate `players/` mount remains Deferred — Mojang co-locates player NBT in the world DB; softcore `players/` trees diverge from that path.

**Known debt / principle:** Reinterpreting a former flat LevelDB directory as a data root creates `{path}/worlds/{name}/` empty while orphaning old `CURRENT`/`.ldb` at the root — silent empty world. Detect and **Warning** at boot if root looks like ZLDB and the new world dir is empty/new. Future path-semantics changes must warn loudly, never reinterpret silently (ops visibility vs silent fallback). Relative `world.path` resolves against `AppContext.BaseDirectory` (DLL dir), never process cwd — same as `zenith.yml`.

**Adendo (jul 2026 — Docker `/data` UX):** PocketMine/Endstone-style **one volume on `/data`**. Image sets `ZENITH_DATA=/data`; config = `/data/zenith.yml`. Entrypoint seeds default config on first boot. **`ZENITH_DATA` is the only supported env override** (data root — not a config matrix). Do not bind-mount over the binary dir (fragile on Dokploy when host file missing → directory). Config changes require container **restart** (read at boot only).

**Adendo (jul 2026 — unified image + ZENITH_DATA owns world):** One product image ([`deploy/image/`](../../deploy/image/)) — binary at `/opt/zenith`, default `ZENITH_DATA=/data`. Platforms (Compose/Dokploy, Pterodactyl egg) are **adapters only** — they set `ZENITH_DATA`, mounts, and panel metadata; no second Dockerfile. When `world.path` is empty/omitted **and** `ZENITH_DATA` is set → LevelDB root = `ZENITH_DATA` (`…/worlds/<name>/`). Empty path without `ZENITH_DATA` stays InMemory (`dotnet run`). Explicit absolute `world.path` still wins. Single [`deploy/zenith.yml`](../../deploy/zenith.yml) template for all hosts.

**Deferred:** Multi-world load, migrator flat→`worlds/<name>`, `players/` volume, Docker Hub automation.

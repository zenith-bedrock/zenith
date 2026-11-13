# ADR §68 — Pterodactyl / Wings hosting (egg + panel hooks)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §68`.

**Choice:** Pterodactyl is a **platform adapter** on the **same product image** as Compose/Dokploy (ADR §20 unified-image adendo) — egg JSON + env, not a second Dockerfile.

1. **Data root:** Wings server files live in **`/home/container`** → egg/startup sets `ZENITH_DATA=/home/container` (Compose uses `/data`).
2. **Image:** [`deploy/image/Dockerfile`](../../deploy/image/Dockerfile) — binary `/opt/zenith`; entrypoint seeds config and optionally evals Wings `${STARTUP}`.
3. **Egg:** [`deploy/pterodactyl/egg-zenith.json`](../../deploy/pterodactyl/egg-zenith.json) — yaml parser on `zenith.yml` for allocation port; `startup.done` = **`Server ready.`**; install seeds shared [`deploy/zenith.yml`](../../deploy/zenith.yml).
4. **Code hooks (panel conventions only):**
   - **`SERVER_PORT`** env overrides `server.port` after yaml load (`ServerConfigOverrides`).
   - **`Server ready.`** log after UDP bind (`RakNetServer.OnListening`).
   - **`server.guid`** under `ResolvePersistentRoot()` (not image dir).
5. **No extra `ZENITH_*` vars** beyond `ZENITH_DATA` + Wings `SERVER_PORT`.

**Why:** Hosting panels are a distribution channel; one image + one yaml SSOT keeps Dokploy and Wings from drifting.

**Non-goals:** official GHCR publish in this ADR (operators build/push locally until tagged); Pterodactyl-specific gameplay; auto-reload config without restart; a second product Dockerfile under `deploy/pterodactyl/`.

**Status (jul 2026):** Shipped — unified image + egg adapter + hooks + docs.

# ADR §60 — Reconnect playerdata (`pd:{uuid}` pose + GameMode)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §60`.

**Choice:** Persist feet XYZ + yaw/pitch + GameMode in the **same world LevelDB** as `inv:` / `ct:`, key `pd:{uuid:D}` lowercase. Blob v1: `u8 version` + 5×`f32` + `u8` mode (Survival=0 / Creative=1). Load on login after inventory, **before** StartGame; Put on quit and after `/gamemode` apply. Unstable identity skips Put. OOB / NaN / bad mode → load miss (flat spawn + config mode) — no silent clamp. Death/void Respawn still world spawn (§40) — only reconnect restores.

**Why:** Softcores (PMMP `players/*.dat`, Dragonfly/PNX `players/` LevelDB) diverge from Mojang BDS, which co-locates `player_` / `player_server_` NBT in the world DB. Zenith keys are already custom; another prefix does not worsen future Mojang import. A new `players/` volume would add ADR §20 path surface and lock into the softcore split. **Reserve** Mojang namespaces (`player_`, `player_server_`, `~local_player`) — never write them in this leaf.

**Flush:** same fire-and-forget + pending-task + shutdown `FlushAsync` as inventory (§41). No mid-tick pose Puts. Unstable identity skips **all** `inv:` / `pd:` Puts (quit + systems).

**Smoke:** quit → rejoin same UUID near last feet; Creative mode survives reconnect. Human Gate A for tags.

**Non-goals:** Mojang `player_*` NBT; `players/` Docker volume; Health/Hunger persist; mid-tick Saves; death-position restore; multi-world playerdata.

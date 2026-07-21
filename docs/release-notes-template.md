## Zenith v0.0.2-alpha

**Product:** `0.0.2-alpha`. Protocol wire unchanged: **1001** / Bedrock **1.26.33**.

Not production-ready — LAN / private feedback only.

### Since 0.0.1-alpha

#### Survival & world
- Floor drops as visible item entities (`AddItemActor` / `TakeItemActor`); AABB pickup + 10-tick delay; partial stack pickup (§26)
- Void → death screen (`DeathInfo` + Respawn handshake); inventory kept; soft-rescue retired (§40)
- Placeable allowlist + palette reverse `runtimeId→name`; place blocked if cell intersects a player standing AABB
- Wire Y honesty (Absolute / MovePlayer eyes = feet + 1.621); Absolute `FLAG_ON_GROUND`; dig idle StopCrack (~10 ticks)
- Tool dig speed (wood→diamond pick/axe/shovel); mid-dig tool swap keeps progress; crack LevelEvent 3602 on speed change
- StackId + DigProfiles foundation (ADR §55)

#### Chests & inventory
- Chest lid BlockEvent + place facing (§28 / §46)
- Double-chest: sneak-place → 54-slot UI; persist 2× `ct:` of 27 (ADR §56)

#### Modes & peers
- `/gamemode creative|survival` via CommandRequest `0x4D` (§52); peers get RemoveActor + AddPlayer (§59)
- Peer sneak / sprint / emote / arm swing (§53)
- Join: full ClientData skin on PlayerList; ClientProfile (XUID / device / platform) on PlayerList / AddPlayer / chat (§49 / §59)
- Mid-game PlayerSkin relay repaired (Encode Id + Session-owned skin)
- Server-authored LevelSound on place / break / hit (no client echo) (§59)

#### Ops & engineering
- `log.server` / `log.raknet` split in `zenith.yml` (§50)
- Overlay + chest SoftCaps; tick Online-once; ISR stack-net-id wire contract (§54)
- SetTitle / toast / play–stop sound packet surface (partial UI helpers)
- Protocol smoke bot — separate repo [`zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) (§58); does not replace human Gate A

### Smoke (Gate A — human Bedrock + protocol bot)

- [x] Tool dig: Creative → diamond pick → Survival → stone faster than empty hand; peer crack; mid-dig swap updates rate
- [x] Double-chest: sneak-place partner → open either half → **54** slots; ISR across halves; restart keeps both `ct:`
- [x] Break one half → dump that half; partner remains single 27; lids close for viewers
- [x] Non-sneak click chest opens (even with held item); sneak + held places on face
- [x] Join skins: A and B join with distinct skins → each sees the other **without** mid-game skin change
- [x] `/gamemode creative` on A → B sees Creative (flight/abilities) **without** B rejoining
- [x] Place/break: B hears block sound; dig hit ticks audible to peers
- [x] Protocol bot: `smoke:first10` + `smoke:wave2` green (offline; LevelDB for persist)

### Still not in this build
- Enchants / durability / tool recipes / gold-netherite / wrong-tool no-drop
- Sand/gravel gravity (ADR §57 written — implement next); Mojang worlds / biomes; `players/` volume
- Floor drops not persisted across process restart (RAM-only)
- Adventure / Spectator; `/` command framework / autocomplete
- Full creative catalogue / `block_state_b64`; 3×3 crafting table
- Hunger / damage / armor; plugins / DI / VisibilitySystem / ECS
- Public production with `auth.accept` including `self-signed` or `offline` (use `[xbox]` only)

### Ops traps (read before deploying)

| Trap | Reality |
|------|---------|
| `world.path` empty | **InMemory** — wiped on restart, **no warning** |
| Relative `world.path` | Under DLL dir (`AppContext.BaseDirectory`), **not** shell cwd. Prefer `.` or absolute (`/app`). |
| `world.path: ./worlds` | Wrong — you get `…/worlds/worlds/<name>` |
| Docker volumes | One persistent volume on **`/data`** (`ZENITH_DATA`). Config + worlds live there. Do **not** mount over `/app`. Restart after editing `/data/zenith.yml`. See [`deploy/README.md`](../deploy/README.md). |
| Stop / redeploy | Prefer SIGTERM so `FlushAsync` runs. Hard kill may keep overlays already in WAL; recent bag/chest Puts can be lost. |
| Auth | Sample often allows self-signed/offline for LAN; use `[xbox]` before any public exposure. |
| Floor drops after restart | Drop entities are RAM-only — gone after process restart (§26). |

### Run

**Docker**

```bash
mkdir -p deploy/worlds
docker compose up --build
```

**Local**

```bash
dotnet run -c Release --project src/zenith
# config: next to the built DLL under bin/.../zenith.yml
```

Artifact on this release: `zenith-v0.0.2-alpha-linux-x64.zip` (framework-dependent publish).

### Docs

- Architecture: `ARCHITECTURE.md`, `docs/architecture.md`
- Gate / roadmap: `docs/alpha-gate.md`, `docs/roadmap.md`
- Decisions: `docs/decisions.md`
- Protocol smoke bot: https://github.com/zenith-bedrock/zenith-smoke-bot

---

## Zenith v0.0.1-alpha (historical — what that tag shipped)

**First public alpha.** Tagged Jul 2026. Protocol **1001** / Bedrock **1.26.33**. Product: **0.0.1-alpha**.

Gate: [`docs/alpha-gate.md`](alpha-gate.md). Release: [v0.0.1-alpha](https://github.com/zenith-bedrock/zenith/releases/tag/v0.0.1-alpha).

### Included on that tag
- Login → flat world + overlay place/break; chat + player visibility; held-item peer sync
- Inventory 36 + ISR rearrange; 2×2 Survival craft; Creative from config (MayFly, palette → cursor)
- LevelDB `c:` / `ov:` / `ct:` / `inv:` under `worlds/<name>/`; chests (single, facing-aware)
- Server-authoritative break timing + dig crack (self + peers)
- Void **soft-rescue** (MovePlayer teleport to spawn; health 20 — not death UI)
- Floor drops as **server cells only** (no `AddItemActor` wire yet)
- Login skins via SkinWire / placeholder (full SerializedSkin join path landed in 0.0.2)
- Graceful shutdown Disconnect + flush; incompatible protocol PlayStatus flush
- Auth `accept` modes; MOTD; stable `server.guid`

### Explicitly **not** on that tag
- `/gamemode`; death–Respawn handshake; drop-entity wire
- Tool dig speed; double-chest 54 UI; sand/gravel gravity
- Plugins / DI / command framework; `players/` volume; Mojang worlds / biomes; hunger tick

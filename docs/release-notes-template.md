## Zenith v0.0.2-alpha (draft)

**Product:** `0.0.2-alpha`. Protocol wire unchanged: **1001** / Bedrock **1.26.33**.

### Since 0.0.1-alpha
- Survival dig speed from curated tools (wood→diamond pick/axe/shovel) — Dragonfly/wiki break formula
- Creative catalog includes those tools; grant in Creative then `/gamemode Survival` to dig faster
- Mid-dig hotbar tool swap preserves progress; crack rate updates via LevelEvent 3602 when needed
- Chest lid BlockEvent + PlaceFacing (prior leaf on develop)

### Smoke (tool dig)
- Creative → diamond pick → Survival → dig stone faster than empty hand; peer sees crack; swap wood→iron mid-dig updates rate without pop-finish

### Still not in this build
- Enchants / durability / tool recipes / gold-netherite / wrong-tool no-drop / gravity / double-chest

---

## Zenith v0.0.1-alpha

**First public alpha.** Not production-ready — LAN / private feedback only.

Protocol wire: **1001** / Bedrock **1.26.33**. Product: **0.0.1-alpha**.

Gate closed Jul 2026 (`docs/alpha-gate.md`): baseline MP 1–12, S35–S41, Dokploy compose, leaf tests, graceful shutdown + hard-kill soft check.

---

### What you can do in this build

#### Join & multiplayer
- Login → resource packs stub → PreSpawn → InGame on a flat world
- Two+ players: see each other (`PlayerList` / `AddPlayer` / `RemoveActor`), move (`MoveActorAbsolute`), chat (`TextPacket` with rate limit)
- Held item visible to peers (`MobEquipment`)
- Skins from login JWT when present (placeholder fallback)
- Chunk streaming as you walk (`ChunkStreamSystem` + overlays)
- MOTD online count; stable LAN identity via persisted `server.guid`

#### World (InMemory **or** persistent)
- **InMemory:** `world.path` empty → RAM-only flat world (wiped on restart, **no warning**)
- **Persistent:** `world.path` set → LevelDB at `{world.path}/worlds/{world.name}/`
  - Flat procedural terrain + sparse overlays (`ov:`)
  - Optional column blobs (`c:`) — virgin explore does not fill disk with identical flat columns
- Place / break with server-authoritative timing; dig crack visible to self **and** peers
- Join while others are building: overlay catch-up so late joiners see edits
- Chests (single, facing-aware) with container UI; contents persist as `ct:`
- Floor drops as **server cells** when inventory is full (no dropped-item entity on the wire yet — peers may not *see* the item entity)

#### Inventory, craft, modes
- 36-slot bag + hotbar; ItemStackRequest rearrange / SHIFT moves
- Survival **2×2** crafting (e.g. log → planks → chest), including sequential CreatedOutput take
- Creative from `server.gamemode: Creative`: MayFly; palette click → cursor; SHIFT → bag; Survival rejects Creative craft
- Inventory persists as `inv:{uuid}` across graceful restart and client quit

#### Survival / safety nets
- Void soft-rescue: teleport camera to world spawn `(0, FlatSpawnY, 0)`; health stays 20 (not full death/respawn)
- Graceful stop (Ctrl+C / SIGTERM): Bedrock `DisconnectPacket` + LevelDB `FlushAsync` before UDP close
- Wrong client protocol: PlayStatus incompatible flushed so the client can show outdated UI

#### Auth & config
- `auth.accept`: `xbox` | `self-signed` | `offline` (LAN-friendly sample; warn on boot if non-xbox)
- `zenith.yml` beside the binary (`AppContext.BaseDirectory`); JSON Schema for IDE autocomplete
- Docker / compose sample under `deploy/` (UDP **19132**, volume for worlds)

#### Engineering (shipped with the product)
- Layering: Gameplay decides → Protocol transmits → Packets serialize → RakNet sends
- Owned leaves: `Zenith.Nbt`, `Zenith.LevelDB`, `src/raknet` (fragment reassembly bounds, session hygiene)
- `dotnet test zenith.sln` green; hot-path BenchmarkDotNet suite in-repo

---

### Explicitly **not** in this tag

- Mojang vanilla worlds / noise biomes; `players/` volume
- Plugins, DI container, `/` commands, `/gamemode`
- Drop-entity wire / general entities / mobs; death–Respawn handshake
- Tool speed / efficiency; sand/gravel gravity; hunger tick
- Double-chest 54-slot UI (facing-only single chest is shipped)
- Public production with `auth.accept` including `self-signed` or `offline` (use `[xbox]` only)

---

### Ops traps (read before deploying)

| Trap | Reality |
|------|---------|
| `world.path` empty | **InMemory** — wiped on restart, **no warning** |
| Relative `world.path` | Under DLL dir (`AppContext.BaseDirectory`), **not** shell cwd. Prefer `.` or absolute (`/app`). |
| `world.path: ./worlds` | Wrong — you get `…/worlds/worlds/<name>` |
| Docker volumes | Mount `zenith.yml` **and** `./deploy/worlds → /app/worlds`. Do not mount over `/app` (wipes the DLL). Redeploy without the worlds volume = wipe. |
| Stop / redeploy | Prefer SIGTERM so `FlushAsync` runs. Hard kill may keep overlays already in WAL; recent bag/chest Puts can be lost. |
| Auth | Sample often allows self-signed/offline for LAN; use `[xbox]` before any public exposure. |

---

### Run

**Docker**

```bash
mkdir -p deploy/worlds
docker compose up --build
```

**Local**

```bash
dotnet run -c Release --project src/zenith
# config: next to the built DLL under bin/.../zenith.yml (created/copied on first run patterns — see docs)
```

Artifact on this release: `zenith-v0.0.1-alpha-linux-x64.zip` (framework-dependent publish).

### Docs

- Architecture: `ARCHITECTURE.md`, `docs/architecture.md`
- Gate / roadmap: `docs/alpha-gate.md`, `docs/roadmap.md`
- Decisions: `docs/decisions.md`

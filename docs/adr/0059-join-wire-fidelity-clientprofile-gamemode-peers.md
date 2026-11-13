# ADR §59 — Join wire fidelity (ClientProfile + gamemode peers + LevelSound)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §59`.

**Choice:** Close the remaining skin-class gaps where login discarded ClientData/identity and join packets lied:

1. **`ClientProfile` on `NetworkSession`** (not `Player`): XUID string, DeviceId, BuildPlatform (`DeviceOS`), PlatformChatId (`PlatformOnlineId`). Parsed at login from identity JWT/chain `xid`/`XUID` + ClientData. Feeds `PlayerList` ADD (XboxUserId / BuildPlatform), `AddPlayer` (DeviceId / BuildPlatform / PlatformChatId), `ChatSystem` TextPacket XUID, and emote relay XUID preference. Same Session-owned wire-state pattern as §49 skin.
2. **`/gamemode` peer honesty:** `GameModeSystem` after self wire → `PlayerVisibility.RefreshPeerView` (RemoveActor + AddPlayer with current GameMode/pose/held + Absolute settle). No PlayerList churn.
3. **LevelSound peer fan-out:** `LevelSoundEventPacket` (0x7b, PM 1001 string sound names) + `WorldProtocol.SendLevelSoundEvent` + `BlockSoundFanout` (all InGame peers, like crack — not Knows-column). `BlockSystem` emits `place` / `break` / `hit` with ExtraData = block runtime id. Inbound LevelSound remains quiet ACK — Dragonfly `ViewSound` model, not client echo.
4. Hygiene: `ResourcePacksInfo` world-template UUID = `Guid.Empty` via `WriteUuid` (no pack product).

**Why:** Peers must see join identity and hear/see block FX without mid-game workarounds. Rule 7: Session profile + existing visibility/fan-out helpers beat VisibilitySystem / sound frameworks.

**Smoke:** Xbox/LAN XUID on PlayerList + chat when present; A `/gamemode creative` → B sees mode without rejoin; A place/break → B hears sound; client LevelSound spam ignored.

**Non-goals:** armor/offhand, EmoteList product, Adventure/Spectator, AvailableCommands, client Animate rebroadcast, sneak BB height, resource-pack product, gravity/biomes (playerdata → §60).

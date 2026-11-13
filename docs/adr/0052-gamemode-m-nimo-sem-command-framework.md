# ADR §52 — `/gamemode` mínimo (sem command framework)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §52`.

**Choice:**

1. Inbound `/gamemode` via **`CommandRequest` (0x4D)** (canal Bedrock) ou `Text` chat (fallback). Args: `survival|creative|s|c|0|1`. Outros `/…` quiet ignore (no registry / `/help` / autocomplete).
2. Valid parse → `Player.SubmitGameMode` (overwrite-latest intent). **Never** `SubmitChat` for slash / command lines — `ChatSystem` must not fan-out commands to peers.
3. `GameModeSystem` on tick: `SetGameMode` (private set; inventory **not** reseeded) → Protocol `SetPlayerGameType` + `SendLocalAbilities` + `SendAdventureSettings`; remint `CreativeContent` on **every** mode change (PocketMine `syncGameMode` → `syncCreative`); Toast feedback via existing `UiProtocol`.
4. Bad `/gamemode` args → same-session Toast (handler decide + transmit; no world mutation). No Gameplay→Packets.

**Why:** H1-3 LAN need — Survival↔Creative in-session without restart/`zenith.yml`. Modes/abilities already exist (§31/§37). At this historical point, one string parse beat a command framework (`dx.md` freeze). The command-freeze portion is superseded by §100; its narrow implementation rationale remains historical.

**Deferred:** Adventure/Spectator; permissions; Bedrock `AvailableCommands` / autocomplete; `/` registry.

**Historical status:** this narrow `/gamemode` decision is superseded for command-surface policy and Bedrock metadata by ADR §100; its original scope and smoke evidence remain historical.

**Smoke:** A in Survival `/gamemode creative` → fly/instant dig + Creative UI; `/gamemode survival` → back; peers do not see the slash text in chat; held/dig follow new mode. No `Unhandled Data Packet: 77`.

**Adendo (jul 2026 — CommandRequest canal):** Bedrock 1.26 envia slash como `CommandRequest` (0x4D), não `Text`. Handler parseia `CommandLine` com o mesmo `GameModeConfig.TryParseCommand` (Text `/gamemode` fica fallback). Origin wire (protocol 1001): origin **string** (`player`, …) + UUID + requestId + **Int64** player unique id (sempre). Sem `AvailableCommands` / `CommandOutput` / palette — quiet ignore outros comandos. Não mergear `dev/commands` framework.

**Adendo (jul 2026 — peer re-AddPlayer):** Closed by §59 — `GameModeSystem` calls `PlayerVisibility.RefreshPeerView` (RemoveActor + AddPlayer + Absolute settle) so peers see the new mode without rejoining. PlayerList is not removed/re-added.

**Adendo (jul 2026 — CreativeContent remint matrix):** Join always sends `CreativeContent` once after `ItemRegistry` (PM PreSpawn `syncCreative`, Dragonfly session start, Serenity spawn) — **including Survival**; the packet seeds the catalog, gamemode/abilities gate the UI. Seeing CreativeContent on Survival join + again on `/gamemode creative` is **expected** (join + remint), not a double-send bug in one stage. Cross-ref: PM remints on every `syncGameMode`; DF/Serenity remint only at join and rely on abilities for mid-session switches. Zenith follows **PM** for remint-on-change.

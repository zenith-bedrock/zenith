# ADR §53 — Pose flags + emote relay + arm swing (peer interaction feel)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §53`.

**Choice:**

1. **Sneak / sprint (Leaf A):** AuthInput bitset → `MovementInputState` (handler queues only) → `MovementSystem` applies `Player.IsSneaking` / `IsSprinting` → dirty `SetActorData` **FLAGS-only** to other InGame peers via `EntityProtocol.SendActorFlags`. No PoseSystem / ECS / VisibilitySystem. Protocol 1001 bit indices (Endstone): continuous `Sneaking=8`; sprint edges `StartSprinting=25` / `StopSprinting=26`. Actor FLAGS: `SNEAKING=1`, `SPRINTING=3`. Mutual exclusion (sprint clears sneak and vice versa) in `MovementSystem`. Spawn / AddPlayer / local seed use full metadata with pose bits OR’d in. Height stays **1.8** this leaf.
2. **Emote (Leaf B):** Inbound `EmotePacket` → validate runtime id == self → rate-limit (1s) → `PlayerVisibility.RelayEmote` (Session helper, like skin) with `FlagServerSide | FlagMuteChat`. EmoteList stays quiet ignore. No EmoteSystem.
3. **Arm swing (Leaf C):** AuthInput `MissedSwing=39` → tick fan-out; dig start / place queue / creative break / `ItemUseOnActor` Attack / UseClickAir → `PlayerVisibility.RelaySwingArm` (immediate). Wire (protocol 1001): action **u8**, runtime id, **data f32** (0), optional swingSource string (`attack` / `mine` / `build`). Inbound client `Animate` stays quiet ignore (no rebroadcast). No combat damage.

**Why:** Peers only saw XYZ+look; crouch/sprint/emote/swing are the minimum LAN “other player is alive” signals. Same decide≠transmit≠serialize path as Absolute (§44) and equipment (§18).

**Deferred:** Swim/glide/crawl flags; sneak BB height (~1.5) + WIDTH/HEIGHT metadata; ActorEvent arm-swing; client Animate relay. (sneak-place-on-chest → §56; LevelSound peer → §59.)

**Smoke:** A holds sneak → B sees crouch; A sprints → B sees sprint; late join while A sneaks → AddPlayer metadata crouch; A emotes → B plays emote; A swings at air → B sees arm swing (Animate 1001: u8 action + runtime + f32 data + optional swingSource string).

**Adendo (jul 2026 — Mojang protocol docs):** Official docs clone at `~/Development/references/bedrock/bedrock-protocol-docs` branch `r/26_u4`. That tip stamps protocol **2169** / game **1.26.50**; Zenith speaks **1001** / **1.26.33**. Use docs for packet field trees; use PM BedrockProtocol 1001 + Endstone headers for AuthInput bit indices and 1001-era Animate swingSource (optional string). Quiet-ACK `SetPlayerInventoryOptions` (`0x133` / 307) — UI prefs only, no product leaf.

**Adendo (jul 2026 — LevelSound peer):** Closed by §59 — server-authored `LevelSoundEvent` (`place` / `break` / `hit`) from `BlockSystem` via `BlockSoundFanout`. Inbound client LevelSound stays quiet ACK (no echo).

## OpenInventory / chest UI (note under §28)

**Superseded for mutation by §54 Phase 2:** handler queues open/close intent; `OpenChest` / `InventoryWindowOpen` apply on GameLoop tick; same-session `ContainerOpen` / contents still Protocol after decide. Slot mutations stay ISR → `InventoryStackIntent` → `InventorySystem`.

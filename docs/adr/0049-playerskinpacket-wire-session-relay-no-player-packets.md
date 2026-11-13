# ADR §49 — PlayerSkinPacket wire + Session relay (no Player→Packets)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §49`.

**Choice:** `SerializedSkin` DTOs stay in Packets (`ref BinaryStream` Write/Read, `PieceType` string, list/image caps). Wire skin state lives on `NetworkSession.Skin` (not `Player` — no Player→Packets). `Player` keeps only `SkinRgba`/`SkinWidth`/`SkinHeight` as classic mirror / PlayerList fallback. Inbound `PlayerSkinPacket` validates UUID == player, updates Session skin + RGBA when classic image is well-formed, relays via `PlayerVisibility.RelaySkin` to other InGame peers (Session helper — same join-like orchestration as `AnnounceJoin`; not a SkinSystem/ECS). `IsVerified` relayed as decoded.

**Why:** Collaborator merge landed broken encode (`BinaryStream` by value), `Player`→Packets leak, self-only echo, and `PieceType` as uint. Chat already forbids handler fan-out loops; skin is rare one-shot so Session helper is enough.

**Smoke:** A changes skin in-game → B sees update. Spoofed UUID ignored.

**Adendo (jul 2026 — Encode Id):** Outbound `PlayerSkinPacket.Encode` must prefix packet Id (`0x5d`) like every other clientbound DataPacket. Missing Id made peer relay silently ignored by Bedrock (inbound Decode unchanged — header already stripped).

**Adendo (jul 2026 — Join skin = product path):** Mid-game change is optional; peers must see the join skin without anyone re-equipping. Parse ClientData JWT via `ClientSkinParser` (Dragonfly `parseSkin` / PocketMine `ClientDataToSkinDataHelper`: SkinData + geometry + cape + animations + persona pieces/tints + flags). `PlayerList` ADD writes full `SerializedSkin.Write`; SkinWire RGBA/placeholder only if parse fails. Endstone/BDS builds `SerializedSkinRef` from `ConnectionRequest` the same way. `TrustedSkin` → PlayerList verified flag.

**Adendo (jul 2026 — ClientProfile):** XUID / device / platform chat id on join → §59 (same Session-owned pattern as Skin).

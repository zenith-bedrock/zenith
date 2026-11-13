# ADR §51 — Collaborator UI/sound/emote packets — wire hygiene (not product feature)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §51`.

**Choice:** Keep Toast / PlaySound / StopSound / ModalForm / ServerSettings / CloseForm / Emote / EmoteList as Packets DTOs. Fix Encode (packet Id prefix), Bedrock wire shapes (SoundPos×8, Emote unsigned runtime + byte flags, EmoteList count/runtime), and expose transmit-only `UiProtocol` + `WorldProtocol.SendPlaySound/StopSound`. Inbound Emote / EmoteList / ModalFormResponse / ServerSettingsRequest stay on the quiet ignore-list (no Systems stub). Round-trips in `zenith.Tests`.

**Why:** Contributor landed DTOs without Id-in-Encode and with broken EmoteList handler decode — unusable outbound and Warning-prone inbound. Wire fix ≠ shipping toast/forms/emote product; H1 smoke stays death/drops.

**Deferred:** Form intent stack; FormId allocator + mid-session refresh (re-send `ModalFormRequest` / ServerSettings with the same id — **not** `NetworkSettings`, which is login compression); product title banners (TextObject / timed UI as gameplay). (Emote peer relay → §53.)

**Adendo (jul 2026 — SetTitle wire):** `SetTitlePacket` (0x58) + `UiProtocol.SendTitle` / `SendSubtitle` / `SendActionbar` / `SendTitleTimes` / Clear / Reset shipped as transmit hygiene. Times is a separate packet from text. Inbound ModalFormResponse remains quiet ignore (no Info decode path). Product title use stays Deferred.

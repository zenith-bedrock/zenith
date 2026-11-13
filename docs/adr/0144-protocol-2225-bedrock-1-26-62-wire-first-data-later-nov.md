# ADR §144 — Protocol 2225 (Bedrock 1.26.62): wire first, data later (nov 2026)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §144`.

**Context:** clients 26.60+ join third-party servers only over NetherNet (§143), and Essential/Cinnabar already serve/target 2225.

**Choice:** bump `ServerIdentity` to 2225 / 1.26.62 and apply the schema deltas for the packets Zenith implements, derived from a structural diff of Mojang's `v1.26.51` and `v1.26.62` JSON schemas (details and table in `docs/protocol-churn.md`). Single-version server: no 2193 fallback. `PlayFabId` leaves `SerializedSkin` for `PlayerListEntry`; `ItemStackRequest` action ids shift after the new `CraftReservedAction`.

**Deferred (blocks calling 2225 complete):** re-extracting block/item/creative/biome/jigsaw data from BDS 1.26.60+, smoke-bot support for 2225, and a retail-client join. Until the data is refreshed, only clients that tolerate 1.26.52-era palettes will behave correctly.

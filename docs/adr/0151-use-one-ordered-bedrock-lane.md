# ADR §151 — Use one ordered lane for Bedrock batches

> Part of the [decision log](README.md).

**Context:** Missing terrain was reproduced using the installed Cinnabar 0.2.1 core: Zenith queued 81 radius-4 columns, but the client received only the 40 sent before spawn release. Remaining terrain used RakNet order channel 1. The go-raknet receive model uses a single ordering queue, so channel-1 order indexes collide with already-consumed channel-0 indexes. Zenith's own multi-channel tests did not establish interoperability.

**Choice:** All reliable Bedrock batches, including post-spawn terrain, use channel 0. Keep asynchronous encoding, bounded world-stream queues and per-tick publication budgets. Supersede only the channel-separation part of ADR §94; its retransmission fix remains required.

**Why:** Delivering terrain takes precedence over isolating head-of-line blocking with an unnegotiated extra channel. No client-name checks, arbitrary delays or unreliable terrain delivery. Protocol/frame observations informed this change; no Cinnabar code or data is copied.

**Invariants:** StartGame precedes terrain; terrain and gameplay batches share a reliable ordered wire lane. A radius-4 view contains 81 distinct columns. Radius negotiation is recognized during pre-spawn, spawn response and play; bounded latest-radius input changes streaming state on the tick. Unsupported camera aim-assist control (321) is recognized without gameplay mutation.

**Non-goals:** Client renderer fixes, plugin APIs, importing Cinnabar data, changing RakNet's general channel implementation, or claiming a rendered-client test from a core-only probe.

**Verification:** Chunk-radius phase/bounds tests, a captured RakNet wire-channel regression, server/transport suites and an isolated offline probe through the installed Cinnabar core. Final transport outcomes and limitations are recorded in `docs/cinnabar-interoperability.md`.

**Status (November 2026):** Implemented in `NetworkSession`, `ChunkRadiusNegotiation` and join handlers.

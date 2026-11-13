# ADR §41 — Void MovePlayer Teleport + shutdown persistence flush

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §41`.

**Choice:** Soft-rescue self → `EntityProtocol.SendMovePlayerTeleport` (`MovePlayer` mode Teleport, cause Command). Graceful stop: `IChunkStorage.FlushAsync` (InMemory noop; LevelDB `WhenAll` pending Puts + `DrainOverlayWrites`) via `World.FlushPersistenceAsync`, then dispose storage, then RakNet. `Program` completes the same shutdown path from **CancelKeyPress**, **SIGINT**, and **SIGTERM** (Docker/Dokploy stop) — 5s flush timeout → Warning. Flush **never** on GameLoop tick (ADR §13).

**Adendo (jul 2026 — shutdown DisconnectPacket):** Before LevelDB flush / UDP close, `GameSessionHost.DisconnectAll("Server closed")` sends Bedrock `DisconnectPacket` (Immediate) + `FlushOutgoing` then RakNet close (no fixed sleep — drain outbound frames synchronously). Abrupt socket death left clients on “host lost” / stuck LAN error UI. MOTD pong trailing `0` matches common Bedrock list clients; RakNet GUID persisted in `server.guid` so LAN identity is stable across restarts.

**Why:** Own camera stayed in void after Absolute-only rescue; Ctrl+C could drop last bag/chest Puts; container SIGTERM previously skipped flush entirely. Operational trust before death/drop-entity surface.

**Smoke:** S39 graceful restart / stop; S40 own-camera snap to world spawn (ARCHITECTURE.md). Ctrl+C with players online → clean disconnect screen.

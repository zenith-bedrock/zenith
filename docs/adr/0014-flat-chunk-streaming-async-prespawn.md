# ADR §14 — Flat chunk streaming + async PreSpawn

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §14`.

**Choice:** `PlayerChunkTracker` + `ChunkStreamSystem` stream missing columns around the player (async `GetOrCreateColumnAsync`, cap starts/tick). PreSpawn loads the spawn disk via `async` continuation (no receive-thread `GetResult`). Still flat base + overlays — not Mojang worlds / noise gen / VisibilitySystem.

**Adendo (jul 2026 — join overlay catch-up):** PreSpawn `RememberMany` right after LevelChunks (before T0 overlay loop). Live `UpdateBlock` fan-out reaches peers with `Chunks.Knows(cx,cz)` even when `!IsInGame`. On InGame enable set `NeedsOverlayResync`; first `ChunkStreamSystem` tick re-emits current overlays for known columns (`ColumnSend.EmitOverlaysToSession`) so places during the LevelChunk batch window are not lost.

**Adendo (jul 2026 — noise join timeout):** `world.terrain=noise` PreSpawn at default `spawn-chunk-radius: 4` generates **81** full columns on first join. Sequential gen was ~54s → client/RakNet timeout while stuck on “waiting for chunk radius”. Fix: **`World.GetRadiusAsync` parallel** (`Parallel.ForEachAsync`, core count) + swallow **`CLIENT_CACHE_STATUS`** in PreSpawn + INFO logs for load/publish. Smoke bot (`smoke:join`) confirms spawn &lt; ~15s on noise after fix.

**Adendo (jul 2026 — PreSpawn center + publish throttle):** PreSpawn loads / publisher / `PublisherCenterChanged` use the **player’s chunk** (from feet XYZ), not hardcoded `(0,0)`. `StartGame` spawn block matches feet. Publish uses **`LevelChunkBatchSize = 1`** (one LevelChunk per GamePacket) + `Task.Yield` between envelopes so noise payloads do not stall the reliable-ordered queue; INFO log reports column count, envelope count, approx payload bytes, and publish ms before `PlayStatus(PLAYER_SPAWN)`.

**Adendo (jul 2026 — join stuck / PM-shaped spawn):** PocketMine sends a **spawn threshold** of chunks then `PLAYER_SPAWN`, streaming the rest; inventory is seeded in PreSpawn. Zenith: (1) **spawn heal** — if saved feet are not clear air (classic flat `pd:` Y=-60 into `terrain=noise`), snap to `SampleSpawnFeetY` + Persist; (2) **ready-disk** before `PLAYER_SPAWN` (superseded by ADR §70 `world.spawn-ready-radius`); (3) inventory + MovePlayer teleport before `PlayStatus`. Fixes client stuck on loading when buried in solid noise.

**Why:** Closes the “world dies outside spawn radius” gap without Actor/EventHandlers or vanilla LevelDB. Matches early Zenith spine: continuous flat multiplayer before effects/gen.

**Deferred (conscious):** food/effects, creative inventory, biomes/noise, Actor/EventHandler frameworks (not mirroring early “everything is an Actor” stacks).

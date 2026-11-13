# ADR §70 — Join terrain contract (client-ready)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §70`.

**Choice:** Hybrid **ready-disk** join — not DF spawn-first with zero LevelChunks, not a full view-radius dump before `PLAYER_SPAWN`.

Client leave-loading prerequisites (wire order SSOT; no `JoinOrchestrator`):

1. **Pose gate** — `World.TryHealSpawnFeet` before StartGame (buried flat `pd:` into noise must not trap the camera).
2. **Jigsaw rules before `StartGame`** — send `JigsawStructureData` (packet 313) with the vanilla Network-NBT compound embedded from Axolotl BedrockData; modern 2193 clients require the structure rule data during join.
3. **Non-empty `BiomeDefinitionList`** — embedded vanilla wire body (`data/biome_definitions.bin`); not a custom biome-JSON product (§66 non-goal stays for *authoring*).
4. **`PLAYER_SPAWN` only after a ready disk** centered on feet: `world.spawn-ready-radius` (default **2**, validate `0..spawn-chunk-radius`). PreSpawn loads/publishes `min(view, spawn-ready-radius)`; tracker view radius stays the client request (capped by `spawn-chunk-radius`).
5. **One column pipeline** — `ColumnSend` + `PlayerChunkTracker` + backpressure for ready-disk and the remaining ring. `ChunkStreamSystem` runs while `Player.IsSpawning` **or** `IsInGame` (stream during SpawnResponse, DF-style terrain-after-spawn-protocol).
6. Inventory seed + MovePlayer teleport before `PlayStatus(PLAYER_SPAWN)` (PM-shaped).

**Clarifies:** §14 PreSpawn/stream adendos; §66 “no BiomeDefinitionList payload” applied to *custom* biome dumps — embedded vanilla list is join contract, not biome product.

**Non-goals (next ADR):** `SubChunkRequestModeLimited` + SubChunk handler; `PLAYER_SPAWN` with zero LevelChunks; AvailableCommands / mid-session gamemode UI; client-side gen / full biome JSON authoring.

**Status (jul 2026):** Shipped — config + embedded biomes + `IsSpawning` stream gate.

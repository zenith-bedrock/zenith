# ADR §42 — Dig crack LevelEvent peer fan-out

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §42`.

**Choice:** Keep `WorldProtocol.SendBlockStartCrack` / `SendBlockStopCrack` session-scoped (Protocol transmits only). Callers use `BlockCrackFanout.Start/Stop(PlayerManager, minerSession, …)` — miner + every other `IsInGame` peer (same Online loop as EquipmentSystem / UpdateBlock). No VisibilitySystem. Dig rarity ≠ §24 hot path.

**Why:** Self-only crack made MP dig look single-player; peers never saw progress. Fan-out is the smallest honest fix without radius culling abstractions.

**Smoke:** S41 (ARCHITECTURE.md). Gate: fail S41 → do not open death/drop-entity.

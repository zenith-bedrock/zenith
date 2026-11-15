# Post-impl audit

**Scope:** Q/inventory drop launch, authoritative item motion, pickup/catch-up at
the physical position, and restart of the persistent server. ADR 163. Earlier
working-tree changes preserved.
**Verdict:** Pass with nits.

## Blockers

- None identified in this scope.

## Nits (non-blocking)

- Air/ground motion uses full-cube world collision. Fluid buoyancy and special
  shapes remain open; this does not claim complete vanilla item physics.
- Automatic nearby-entity merging remains open. Tosses allocate fresh actors so
  an old pile cannot swallow the animation. Existing bounded deposit capacity
  still refuses an operation atomically rather than losing inventory.
- The installed-core probe proves server/core packets and inventory conservation,
  not frames from the Rust renderer.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| World/FloorDropMotion, FloorDropStore | Physical pose separate from bounded custody key; snapshot/rollback preserve both | Yes |
| Gameplay/Inventory/InventorySystem | Resolve player yaw/pitch and stage throw with inventory commit | Yes |
| Gameplay/WorldInteraction/FloorDropPhysics, FloorDropSystem | Gravity/drag/collision, pose replication and physical pickup | Yes |
| Gameplay/Replication/FloorDropFanout | Fresh thrown actors, physical partial republish and stationary merge guard | Yes |
| World/World | Prevent collision-state eviction while a live item occupies the chunk | Yes |
| Session/NetworkSession, ChunkStreamSystem | Bounded worker completion handoff; tick-owned item catch-up after terrain | Yes |
| Protocol/ColumnSend | Publish current owner-decided position/velocity, filtered by physical chunk | Yes |
| tools/cinnabar-terrain-probe | --drop scenario through installed core and Windows endpoint-file retry | Yes |

## Freeze / anti-pattern scan

- [x] No new frozen abstraction without ADR: no actor/ECS/plugin/physics scheduler introduced.
- [x] No Network/ revival.
- [x] Handler/thread rules OK: only the tick changes bodies/pickup; terrain worker no longer enumerates floor entities.
- [x] Intent queue policy OK: existing discrete FIFO and atomic deposit/rollback remain.
- [x] Completion handoff bounded to 64 with cancellation/backpressure; no silent loss of column acknowledgements.
- [x] Motion snapshots reuse a scratch list in the tick instead of allocating two arrays every tick.

## DX / tests

- `dotnet test zenith.sln -c Release --no-restore -m:1`: **1,875 passed**,
  zero failed/skipped (1,565 zenith.Tests + 310 leaf). Includes architecture checks.
  Log: Builds/zenith-test-runtime/drop-toss-verified.log.
- Tests establish elevated launch and cardinal/pitch direction; flight, landing,
  wall/ceiling collision; pickup at landing instead of custody anchor; partial
  republish at actual pose; fresh actors; collision-state retention; owner-only
  column catch-up and physical chunk filtering.
- Existing wire test now checks that the transmitted velocity is the stored body's
  velocity; independent direction/trajectory tests establish the numerical behavior.
- Final publication at Builds/publish/zenith/release. Same persistent world
  Builds/data/worlds/world and active server identity retained. No world reset.
- Old test process did not exit on its normal shutdown signal; the first replacement
  encountered the still-locked database. Only our identified old PID was terminated;
  the new process then opened the same LevelDB successfully. Native log-file
  redirection now belongs to the hidden launcher rather than a transient shell pump.
- Final real-core --drop scenario: AddItemActor above feet, forward velocity,
  **19 absolute pose updates**, **3.409 blocks travelled**, grounded landing,
  TakeItemActor + RemoveActor and original count 64 restored. No block edits.
  Log/result: Builds/zenith-test-runtime/live-drop-toss-final-2.
- Nine-stage terrain route also passed after the completion-handoff change:
  81/81 at join/move/re-entry/expansion and 25/25 at reduced radius.
  Result: Builds/zenith-test-runtime/live-drop-terrain-final.
- Probe/Release publication compile without warnings. Windows can briefly expose
  session.addr while the core holds its write handle; the probe retries that read.
- Scoped diff has no whitespace errors. The full working tree currently flags
  trailing whitespace in unrelated data/mobs.json; that file was left untouched.

## Should not be committed

- Builds/, probe identities/logs, executables, world databases or local config.
  No Cinnabar code/fixtures/assets copied. No automatic commit.

## Suggested follow-ups

1. Confirm visible trajectory in the rendered Cinnabar client.
2. Add fluid behavior and nearby item merging when those gameplay cases are tackled.

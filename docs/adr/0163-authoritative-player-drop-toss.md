# ADR §163 — Authoritative player-drop toss

Q/inventory drops spawn above the player's feet with yaw/pitch launch direction.
Use the observed PocketMine facts (player Y + 1.3, direction × 0.4, pickup delay 40;
item drag 0.02 and gravity 0.04) without copying its implementation. The existing
FloorDropSystem advances these bodies and projects absolute positions; sending
only AddItemActor velocity is insufficient for clients that interpolate server poses.

FloorDropStore retains its bounded custody-cell handles and rollback contract.
Optional motion belongs to a stack/entity, independently of its custody handle.
Pickup, partial republish and late-join publication use the physical pose. Thrown
batches create fresh actors so an existing pile cannot swallow the launch. New
static deposits may merge only with spatially coincident stationary stacks.
Air drag, gravity, full-cube swept collision and ground friction cover this change;
fluid buoyancy, automatic nearby-entity merging and special block shapes remain open.
Chunks containing live floor entities retain collision/overlay state until the
items are removed, so residency eviction cannot turn a persisted platform into air.

Terrain workers send terrain/overlays only. Their bounded column completion
handoff is drained on the tick to publish current floor items after LevelChunk.
This keeps item lifetime/movement/catch-up on the owner and avoids asynchronous
enumeration of a changing floor store. No new actor framework or physics scheduler.

Tests must establish elevated launch, cardinal/pitch direction, movement/landing,
wall collision, pickup at the actual position, merge rejection without partial
inventory commit, and late-join/current-pose publication. Preserve the existing
bounded capacity, pickup delay and stack metadata invariants.

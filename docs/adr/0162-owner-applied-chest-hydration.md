# ADR §162 — Apply persisted chest hydration on the gameplay owner

The parallel column loader must not mutate ChestStore's dictionaries or slot
arrays. Cold joins against a persisted world exposed simultaneous first-touch
chest writes from generation workers, corrupting the store and aborting terrain
streaming. More locks on gameplay stores would preserve the wrong ownership.

Workers retain storage-owned chest blobs as pending data in the existing per-chunk
hydration map. The tick applies a column's pending chest seeds before publishing
that column. Seeds never replace an already live chest; explicit load/rollback
APIs remain separate. Pending data survives cancellation of one viewer, and is
discarded on eviction so the next touch reads persistence again. There is no new
system, unbounded result queue, or blocking disk read on the tick.

The hydration map keeps ownership of bytes until their one-time tick application;
after application its entry is the empty list. Overlays retain their existing
concurrent seeding path. Pre-spawn failure logging includes the exception stack
so other failed collection paths can be diagnosed rather than hidden.

Also correct RakNet Disconnect's mistaken ConnectedPong ID: server teardown must
emit 0x15, not a one-byte 0x03 that the peer parses as a truncated pong.

Verify concurrent cold chunks, no ChestStore writes before tick application,
seed-once/live-edit preservation, cache re-entry, eviction and wire disconnect ID;
then rerun the installed Cinnabar core against the persistent world.

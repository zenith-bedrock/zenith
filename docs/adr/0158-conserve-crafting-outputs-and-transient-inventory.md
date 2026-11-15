# ADR §158 — Conserve crafting outputs and transient inventory

All recipe outputs remain owned until transferred or dropped. PlayerCraftUi stages
the indexed results of one craft and selects the requested Create result into the
existing shared CreatedOutput slot. Selecting another result does not overwrite
the untaken one. New crafts cannot replace outstanding results; rollback and death
loot include the complete result set. Single-output registry callers must refuse
multi-output recipes before consuming ingredients.

Live inventory saves contain the main bag and cursor/grids/remaining results in one
versioned envelope under the existing inventory key. Old slot blobs remain readable.
Login restores the private player before publication and moves transient items into
available bag space. Overflow remains in the saved UI custody and is recovered when
idle bag space becomes available; it is never replaced by unpersisted ground loot.
Recovery cannot run across queued UI requests or an open window.

No generic transaction framework, new scheduler or new storage key is introduced.
The inventory record is the conservation boundary for these slots. Armor/offhand and
player-data saves retain their existing lifecycle. Existing write-behind durability
semantics are unchanged; this is not a new power-loss guarantee.

Verify manual/automatic multi-result crafts, invalid indices, partial failure,
replay, pending-result death, legacy blob reads, disconnect/reload, full bags and
repeated recovery. Server stays stopped at the user's request.

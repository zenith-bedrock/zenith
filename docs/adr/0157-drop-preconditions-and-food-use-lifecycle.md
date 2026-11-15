# ADR §157 — Drop preconditions and food-use lifecycle

Cinnabar's normal world-drop transaction may omit a legacy request ID when it
empties the source slot. Accept that conservation shape without inventing an ISR
response, and bind every legacy drop to its original authoritative slot snapshot.
The gameplay tick rejects stale/duplicate source snapshots before removing items;
correlated requests retain the existing replay ledger and responses.

Food air-use starts a timed use of one explicit hotbar stack. It must not consume
immediately or search other inventory slots for a matching item. The hunger tick
owns start, cancellation, selected-stack revalidation and completion. Repeated
start signals cannot reset an active use; release, selection/stack changes, death
and disconnect cancel it. Preserve creative inventory and use real food nutrition,
saturation and duration data, with container returns where applicable.

Use bounded concrete food-use intents and a small per-player active-use value,
within the existing hunger system. No additional runtime dispatcher is introduced.
Tests cover wire drop shapes, stale/repeated input, delayed consumption and exact
slot mutation. Server remains stopped at the user's request; no live-client parity
claim is made from these automated tests.

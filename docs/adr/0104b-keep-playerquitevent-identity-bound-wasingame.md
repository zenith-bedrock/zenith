# ADR §104b — Keep PlayerQuitEvent identity-bound; WasInGame is a gameplay-entry predicate

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §104b`.

**Choice:** retain one `PlayerQuitEvent` for the teardown of a `Player` whose identity was
accepted and registered in `PlayerManager`. It is published exactly once from
`NetworkSession.HandleClose`, after Session-owned peer removal/persistence handoff and after the
player is removed from `PlayerManager`. `WasInGame` remains the captured fact that the player
completed the spawn transition before that teardown. `PlayerQuitEvent` is not published for a
transport session that fails before a `Player` exists. Do not add `PlayerDisconnectedEvent` now.

**Why:** the two facts are not two independent lifecycle owners or cleanup paths. A transport
close can happen in every login phase; once a Player is bound, all causes (client disconnect,
server kick, timeout, gameplay pre-spawn failure and coordinated shutdown) converge in the same
idempotent RakNet close → listener removal → `HandleClose` path. `WasInGame` answers the single
consumer question that exists today: whether the player was ever made visible to peers, so a
leave chat line is valid. Cleanup/persistence must stay direct and ordered at the Session
boundary, not become event listeners. There is no current second consumer of a disconnection
event that would justify another event type or a lifecycle-event framework.

**Lifecycle audit:** before identity acceptance, authentication/protocol/login rejection leaves
no Player and emits neither event nor persistence. After registration but before spawn, graceful
disconnect, timeout, kick, pre-spawn load failure and shutdown persist stable inventory/player
data, queue chest-opener release for the GameLoop and publish `PlayerQuitEvent(WasInGame=false)`.
After `InGameSessionHandler` enters gameplay, the same path first removes peer visibility and
publishes `PlayerQuitEvent(WasInGame=true)`. UDP/network loss is observed as RakNet timeout;
there is no separate socket-failure callback to model. RakNet's close hook is at-most-once, and
`ZenithSessionListener` removes the session under its gate before calling `HandleClose`, preserving
exactly-once game cleanup even if close is requested from more than one context.

**Compatibility:** `PlayerQuitEvent` historically sounded like a completed-gameplay quit, but
its actual established contract is identity-bound teardown; consumers must not infer `InGame`
without checking `WasInGame`. `PlayerLoginEvent` is similarly acceptance/registration-time, not
a spawn-complete event. A future concrete consumer that needs every identity-bound transport
close may add `PlayerDisconnectedEvent` only with that consumer and explicit ordering relative
to this direct cleanup; it must not be used for raw pre-login connections because no `Player`
identity exists.

**Non-goals:** lifecycle hierarchy/base event, generic disconnect reason bus, moving visibility,
chest release or persistence into EventBus listeners, changing PlayerManager ownership, or making
network loss a Gameplay decision.

**Adendo (ago 2026 — inventory/player-data persist race):** the "persist stable inventory/player
data" step above was, until now, a direct synchronous call from `NetworkSession.HandleClose` —
running on the network thread, not the GameLoop. `PlayerInventory`'s backing array has no lock, so
this could read a torn snapshot while `InventorySystem`/other GameLoop systems concurrently mutated
that same player's inventory on the GameLoop thread (found via a reference-parity audit; a real
duplication/loss risk on disk, not hypothetical). Fixed to match the chest-opener release already
described above: `HandleClose` now only queues (`PlayerManager.SubmitDisconnectedInventoryPersist`),
and `InventorySystem.Tick` drains it (`TryConsumeDisconnectedInventoryPersist`) on the GameLoop
thread that actually owns the mutation. This does not change the "direct... not EventBus listeners"
non-goal above — the queue is the same plain hand-off mechanism the chest release already used, not
an `EventBus` subscription. One additional wrinkle specific to persistence (chest-opener release has
no such requirement, since in-memory opener state does not need to survive process exit): a graceful
shutdown's `_lifetime.Cancel()` can race the GameLoop's own cancellation check such that no further
tick ever drains a queue entry enqueued by `DisconnectAll`'s in-line `HandleClose` calls. `ZenithServer.
ShutdownCoreAsync` now drains this queue explicitly, directly, right after `await gameLoop` completes
— at that point the GameLoop is provably no longer running, so a direct call there is race-free by
construction, without needing one more scheduled tick.

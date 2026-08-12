# Disconnect lifecycle

This reference records the current, complete teardown contract. It is intentionally concrete:
there is no generic lifecycle-event family.

## Ownership and terminal path

`RakNetSession.Close` is idempotent and calls `ZenithSessionListener.OnSessionClose` at most once.
The listener removes the `NetworkSession` from its locked map before calling
`NetworkSession.HandleClose`. Therefore a timeout, client disconnect, server disconnect/kick,
pre-spawn failure or shutdown all converge on one teardown invocation.

`HandleClose` is a Session-lifecycle boundary. For a bound Player, it performs this ordering:

1. Capture `Player.IsInGame` as `wasInGame`.
2. If true, transmit peer visibility removal to the current in-game peer snapshot.
3. Queue an open-chest release to `InventorySystem`; only the GameLoop mutates `ChestStore`.
4. Fire-and-forget stable-identity inventory and player-data writes.
5. Set `IsInGame` false, remove the Player from `PlayerManager`, then publish `PlayerQuitEvent`.
6. Disable the active session handler and clear the RakNet game-identity marker.

The persistence writes are awaited only by the coordinated shutdown flush. A disconnect never
holds a lock across disk I/O or protocol send.

## Phase matrix

| Phase at close | Player exists | `PlayerQuitEvent` | `WasInGame` | Cleanup/persistence |
|---|---:|---:|---:|---|
| Handshake, protocol/auth rejection | no | no | — | transport/session only |
| Accepted login through resource pack, pre-spawn or spawn response | yes | yes | false | remove manager entry; stable data persist; chest release handoff if needed |
| In-game | yes | yes | true | same cleanup plus peer visibility removal |

`PlayerLoginEvent` is emitted after acceptance/registration, before the spawn sequence. It is not
a spawn-complete event. `InGameSessionHandler.OnEnable` is the completion point for `IsInGame`.

## Why there is no PlayerDisconnectedEvent

`WasInGame` does not encode a different disconnect cause. It says whether the already-bound
player crossed the gameplay visibility boundary before the one transport teardown occurred.
Today the only event consumer is `PlayerPresenceAnnouncer`, which needs exactly this fact to
avoid announcing a departure for a player peers never saw join. Persistence, player removal,
visibility ordering and chest ownership are not event consumers: they are required Session and
Gameplay cleanup.

Adding an otherwise unconsumed `PlayerDisconnectedEvent` would duplicate the same publisher and
force consumers to reconstruct the existing ordering without solving a present need. If a future
feature has a concrete need to observe every *identity-bound* disconnect independently of a
gameplay quit, introduce the event alongside that consumer and specify its ordering. Raw
pre-login transport failures still cannot carry a Player event.

## Known semantic compatibility

The name `PlayerQuitEvent` may suggest gameplay-only departure. Its established behavior is
identity-bound teardown, so consumers must check `WasInGame` before assuming peer visibility or
a completed spawn. This is documented in ADR §104b; no wire behavior or persistence key changes.

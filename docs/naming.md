# Naming conventions

Zenith names should make **layer** and **outcome** obvious. This is the SSOT for verbs; folder roles stay in [`ARCHITECTURE.md`](../ARCHITECTURE.md) / [`AGENTS.md`](../AGENTS.md).

Agents: also [`.cursor/rules/naming.mdc`](../.cursor/rules/naming.mdc).

---

## 1. Prefix vocabulary

| Prefix | Returns | Meaning | Typical home |
|--------|---------|---------|----------------|
| `Handle*` | `void` | Inbound packet path: decode → validate → `Submit*` (no world mutate) | `Session/Handler` |
| `Submit*` | `void` / `bool` | Enqueue intent / overwrite-latest input | `Player/` |
| `TryConsume*` | `bool` | Drain one pending intent/input | `Player/` |
| `Apply*` | `void` / `bool` | System applies an intent; may mutate domain if gates pass | `Gameplay/Systems` |
| `Try*` | `bool` | Fallible op; `false` is expected (not exceptional) | any layer |
| `Is*` / `Can*` | `bool` | Predicate only — **must not** mutate | any layer |
| `Send*` | `void` | Build DTO + transmit via session | `Protocol/` only |
| `Relay*` | `void` | Fan one player’s effect to other InGame peers | `Session/` helpers |
| `Announce*` | `void` | Join/leave (or similar) ordered multi-packet sequence | `Session/` helpers |
| `*Fanout` (type) | static helpers | Multi-peer FX (crack, lid, sound) | `Gameplay/` |
| `Begin*` / `Abort*` / `Complete*` | `void` | Lifecycle start / cancel / finish | `Player/` + systems |
| `Mark*` | `void` | Refresh a timestamp or flag without changing phase | `Player/` |
| `Remember*` | `void` | Snapshot last-replicated wire state after a successful fan-out | Systems |
| `Parse*` / `TryParse*` | value / `bool` | Pure decode from JWT/bytes/JSON | `Session/` parsers |

`Refresh*` is allowed for “re-sync peer view of existing subject” (e.g. `RefreshPeerView` after `/gamemode`) — prefer it over inventing a second `Announce*`.

---

## 2. Conditional tick side-effects — **no `Maybe*`**

Tick hooks that often no-op must name **what happens when the condition holds**:

| Avoid | Prefer |
|-------|--------|
| `MaybeAbortIdleDig` | `AbortIdleDigIfStale` |
| `MaybeUpdateDigTool` | `UpdateDigToolIfHeldChanged` |
| `MaybeSendX` | `SendXIfDirty` / `TrySendX` |

Patterns:

- `*IfStale` / `*IfDirty` / `*IfHeldChanged` — condition in the suffix
- `Try*` — when the caller cares about bool success
- Early `return` inside is fine; do not encode uncertainty in the verb (`Maybe`, `Possibly`, `Sometimes`)

---

## 3. Layer-linked rules

```text
Session/Handler     Handle*  →  Submit* / TrySubmit*
Gameplay/Systems    Apply* / Abort*If* / Update*If*  →  Protocol.Send* / *Fanout / Relay*
Protocol/           Send* only (no Decide)
Packets/            Encode / Decode / Read / Write (no Send/Apply)
Player/             Submit* / TryConsume* / Begin* / Abort* / Mark*
```

Violations to reject in review:

- `Send*` on a System or Handler (call `session.Protocol.*.Send*` instead)
- `Apply*` on a Handler (queue an intent)
- `Handle*` on a System (systems are not packet routers)
- `Is*` / `Can*` that mutate state

---

## 4. Types and helpers

| Form | Use |
|------|-----|
| `*System` | GameLoop tick participant (`IGameSystem`) |
| `*Protocol` | Session-scoped transmit façade |
| `*Packet` | Wire DTO in `Packets/` |
| `*Fanout` | Static peer FX helper in `Gameplay/` |
| `*Parser` | Pure parse (`ClientSkinParser`, `ClientProfileParser`) |
| `*Intent` / `*InputState` | Queued decide inputs |

---

## 5. Examples in-tree

```csharp
// Handler
HandleAuthInput → SubmitMovementInput / SubmitDigStart / MarkDigActive

// System
ApplyDig → BlockCrackFanout.Start
AbortIdleDigIfStale → BlockCrackFanout.Stop + AbortBreak
UpdateDigToolIfHeldChanged → RetargetBreakTiming + UpdateSpeed

// Protocol
SendMoveAbsolute(..., FLAG_ON_GROUND)
SendLevelSoundEvent(...)

// Session helper
AnnounceJoin / RelaySkin / RefreshPeerView
```

---

## 6. Migration

When touching a file that still uses `Maybe*`, `Do*`, or a mutating `Check*`, rename in the same PR. Do not mass-rename the tree for cosmetics alone.

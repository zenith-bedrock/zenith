# ADR §78 — EventBus first domain consumers — join/leave chat

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §78`.

**Choice:** Prove the `EventBus` seam (§21) holds for real Gameplay consumers, not just login/quit publishers with nobody listening, **before** any plugin surface is discussed. Scope stays inside §21's own boundary: no new event types, no `Unsubscribe`, no priority, no plugin loader.

1. **`PlayerPresenceAnnouncer`** (`Gameplay/PlayerPresenceAnnouncer.cs`) — static, composition-internal, same shape as `FloorDropFanout`. `OnLogin` broadcasts `"{name} joined the game"`; `OnQuit` broadcasts `"{name} left the game"` **only when `PlayerQuitEvent.WasInGame`** — a pre-spawn drop (still in login/resource-pack/spawn) never had a "join" anyone saw, so it does not get a "left" line either. Both use the **existing** `PlayerLoginEvent`/`PlayerQuitEvent` types — §21 explicitly deferred *new* domain event types, not new listeners on the two that already ship.
2. **`PlayerQuitEvent` gains `WasInGame`** (constructor param, not a new type) — `NetworkSession.HandleClose` captures `Player.IsInGame` before it flips to `false`, so the field reflects "did anyone actually see this player" rather than "did a `Player` object exist."
3. **`ChatProtocol.SendSystem(message)`** added alongside the existing `SendChat` — a server-authored line (`TextPacket.TypeSystem`, no `SourceName`) distinct from player-authored chat.
4. **Registration lives in `ZenithServer`** (the composition root), not inside `EventBus` or `PlayerManager` — `eventBus.Subscribe<PlayerLoginEvent>(...)` / `Subscribe<PlayerQuitEvent>(...)` sit next to `gameLoop.Register(...)` calls, same pattern as every other system wire-up.
5. **`EventBusTests.cs`** (new) — the shipped infra had **zero** test coverage until now. Covers: no-op with no listeners, multiple listeners fire in subscribe order, listeners are scoped per event type, a throwing listener does not block later ones (the isolation `Publish`'s doc comment already claimed), and Subscribe-during-Publish only takes effect on the *next* `Publish` call. `PlayerPresenceAnnouncerTests.cs` covers the production wiring: join broadcast excludes the joining player (not yet in-game), quit broadcast excludes the leaving player, pre-spawn drops broadcast nothing.

**Why now:** `comparison.md` already commits Zenith to "extension points sit on Gameplay, not raw packet listeners" as the answer to plugin-API pressure from comparable/newer projects (Basalt in particular — see `roadmap.md` "Competitive read"). That claim was untested — `EventBus` had never been exercised with more than the zero consumers it shipped with. This ADR is the proof, not the plugin API itself.

**Clarifies:** §21 ("Deferred: Domain event types beyond login/quit" — still true, no new types added here); `roadmap.md` Yes-next priority 6.

**Non-goals:** Plugin API / external assembly loading (still frozen, §21); new event types (join/leave chat reuses the two that exist); `Unsubscribe` / listener priority; moving the existing inline `PlayerVisibility.AnnounceLeave` / `ChestLidFanout.ReleaseOpener` cleanup in `NetworkSession.HandleClose` onto `EventBus` — those are ordering-sensitive and out of scope for a small, reversible proof.

**Status (ago 2026):** Shipped — `PlayerPresenceAnnouncer` + `PlayerQuitEvent.WasInGame` + `ChatProtocol.SendSystem` + `ZenithServer` wiring + `EventBusTests` + `PlayerPresenceAnnouncerTests`.

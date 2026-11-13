# ADR §47 — MOTD online count + ghost session hygiene

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §47`.

**Choice:**

1. `RakNetServer.OnlinePlayerCount` (`Func<int>?`) wired to `PlayerManager.Count` — MOTD online is players, not `ConnectionCount` (avoids `Connections.ToList()` on ping).
2. Bedrock `DisconnectPacket` → `session.Disconnect()` in PreSpawn + InGame (closes transport + `HandleClose`).
3. At `MaxConnectionsPerAddress`, OCR2 **evicts** one same-IP session (`HasGameIdentity == false` preferred, else oldest `LastSeen`) then accepts — does not only reject.
4. Login displace: if `TryAdd` fails, disconnect existing same username and retry.

**Why:** Quit without RakNet teardown left UDP sessions until 15s timeout; MOTD inflated; rapid rejoins from new ephemeral ports burned `max-players-per-ip` (default 3) while the host still saw one in-world player.

**Deferred:** Shorter global idle timeout; kicking live NAT roommates beyond unbound-prefer (RakNet digest mute → §50).

**Smoke:** Peer leaves → MOTD shows 1 (host). Rejoin repeatedly from same IP without wedging at 3.

# Bedrock 2193 data refresh and NetherNet implementation audit

**Date:** 2026-10-12  
**Scope:** Stable Bedrock protocol 2193 data refresh, independent `Zenith.NetherNet` transport, and session login compatibility.

## Applied

- Refreshed the embedded block, item, creative-content, and biome assets for Bedrock 1.26.52 / protocol 2193. Added an asset manifest and offline importer checks. Existing numeric IDs in saved worlds and player data are not migrated; the operator plans to reset the world and create fresh files for the updated data.
- Added `src/nethernet/` as the independent `Zenith.NetherNet` leaf project. It owns Mojang full-ICE HTTP signaling, libdatachannel WebRTC peers, persistent operator identity assertions, SDP client-token/fingerprint checks, and the one-byte reliable/unreliable framing.
- Added `src/zenith/Session/GameTransportConnection.cs` as the session adapter. Both transports now carry the same existing Bedrock batches; NetherNet omits RakNet's game-message identifier. No Bedrock game packet was added for NetherNet.
- Added `network.nether-net` listener, join URL, ICE bind address, optional ICE servers, and per-peer ICE UDP range settings. Zenith keeps the requested NetherNet transport selected, validates its settings, and reports signaling errors in the server log.
- Updated architecture, configuration, technical-reference, and decision records. The operator key `nethernet-operator-key.pk8` must persist with the server data root so clients retain the same trust identity.

## References

- [Mojang NetherNet onboarding guide](https://github.com/Mojang/bedrock-protocol-docs/blob/main/additional_docs/NetherNetOnboardingGuide.md)
- [axolotl-pm/NetherNet](https://github.com/axolotl-pm/NetherNet)
- [df-mc/go-nethernet](https://github.com/df-mc/go-nethernet)

## Verification

- `dotnet build zenith.sln --no-restore -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false` — passed, zero errors. Three pre-existing xUnit analyzer warnings remain.
- `dotnet test zenith.sln --no-build --no-restore -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false` — passed, 1,415 tests across all eight test projects.
- NetherNet leaf coverage includes identity signatures, frame limits/reassembly, signaling request validation and lifecycle, full ICE negotiation, both channel modes, and bidirectional frame delivery.
- Used the installed smoke-bot dependencies (`bedrock-protocol` 3.60.1, `nethernet` 1.1.2) against a temporary local Zenith server through an HTTP signaling adapter. A 1.26.51 client (protocol 2193) received `network_settings`, `login_success`, `resource_packs_info`, `start_game`, chunks, and `spawn` over NetherNet. This exercised offline auth; it confirmed that the server answer carried `a=identity` but did not exercise retail Bedrock's TOFU prompt or Xbox identity validation.

## Limits

- For remote peers, configure a reachable host or server-reflexive ICE candidate and open the configured UDP range. HTTP reverse-proxying the signaling endpoint does not proxy WebRTC traffic.
- The smoke bot is not the retail graphical client. Its success verifies server transport, protocol login, and spawn, but does not reproduce the previously reported Minecraft UI screen.

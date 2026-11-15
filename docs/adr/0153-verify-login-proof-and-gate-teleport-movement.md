# ADR §153 — Verify login proof and gate movement after teleports

**Context:** The fourth packet audit found that RakNet FULL login trusted unverified claims,
unknown authentication alternatives bypassed policy, and predicted positions could overwrite
an authoritative teleport before its acknowledgement.

**Choice:** Reuse FranchiseTokenValidator and its expiring key cache for FULL game login on
both transports. Prepare/refresh keys outside receive callbacks; absent/expired keys fail closed.
Verify ClientData ES384 with the P-384 key in the authenticated token's cpk claim before identity
or profile parsing. FULL identity never falls back to an unverified legacy certificate. Keep
SELF_SIGNED/offline policy explicit; do not pretend a structural certificate check proves trust.
Reject unknown AuthenticationType before policy branches. Bound Login parsing by the unsigned
length of Connection Request and require complete consumption of both windows.

Gameplay marks respawn/dismount teleports pending before sending. PlayerIntents preserves a
HandledTeleport acknowledgement across overwrite-latest movement updates, clearing queued
inputs/acknowledgements when a new teleport begins. MovementSystem ignores predicted positions
until acknowledgement. Network handlers publish the acknowledgement only. Initial spawn uses
the existing initialization barrier: inputs discarded during spawning cannot overwrite its pose.

**Boundaries:** No new scheduler, auth interface, transport dependency in gameplay, or plugin API.
The existing issuer validator remains Session-owned. This targets modern FULL tokens; legacy
certificate-only authentication is not silently accepted as modern FULL authentication.

**Verification:** Synthetic RSA issuer and P-384 client keys, invalid token/claims/key/signature,
unknown auth alternatives and malformed Login windows; movement before/after acknowledgement,
pending and overwritten inputs, respawn and dismount. No real credentials in fixtures.

**References:** The primary [gophertunnel login API](https://pkg.go.dev/github.com/sandertv/gophertunnel/minecraft/protocol/login)
documents ClientData signed by the connection key and issuer-verified identity. The pinned
[Login wrapper](https://github.com/Sandertv/gophertunnel/blob/v1.63.0/minecraft/protocol/packet/login.go)
uses the byte-slice connection request. No reference implementation source was imported.
The issuer's [OpenID discovery metadata](https://authorization.franchise.minecraft-services.net/.well-known/openid-configuration)
confirms RS256, the configured issuer and JWKS URL. The public JWKS was reachable during
verification; no user token or credential was used to query it.

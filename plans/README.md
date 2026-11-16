# Implementation plans

Internal extensibility audit generated with improve on 2026-11-16. Existing dated reports in this directory are separate audits and remain unchanged.

| Plan | Priority | Effort | Depends on | Status |
|---|---|---|---|---|
| [001 — Internal extension spine](001-internal-extension-spine.md) | P1 | L across phases | none | TODO |
| [002 — Environment change policy](002-environment-change-policy.md) | P2 | M across phases | 001 phases 0-3 | IMPLEMENTED (rev 4), all phases and per-region cause flags done; no open items |
| [003 — Bedrock parity: XP orbs and falling blocks](003-bedrock-parity-xp-orbs-and-falling-blocks.md) | P2 | S + M | none | phase B done, phase A (XP orbs) next |

Phase dependency: inventory -> permissions -> 2b single-cell policy -> 2c batch planning -> commands -> narrow ports/Protect example -> public-surface ADR only. Phase 2a (presence and immutable payloads) follows inventory, may run alongside permissions, and precedes 2b. Keep 2a and 2c in separate PRs; L/HIGH is concentrated in 2c. The small command-catalog registration fix may land independently before permissions. Help/sample hygiene is a separate parallel PR and does not block permissions. Protect is the first proof; economy is deferred until a real ledger and transaction model exist.

## Findings considered and rejected

- ClientPorts are already a stable authoritative plugin surface: rejected; they are client output channels and contain internal/wire concepts.
- EventBus currently has a concurrent-subscribe bug exercised by production: not established; registrations are composition-time. Caller-thread semantics remain an extension constraint.
- GameRules.TrySet is a current data race: rejected; synchronization and snapshot publication are explicit. Future owner ordering requires a deliberate design choice.
- Every world mutation must immediately be routed through a universal protect/event framework: rejected; first proof explicitly covers player actions, with environmental protection deferred.

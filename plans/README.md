# Implementation plans

Internal extensibility audit generated with improve on 2026-11-16. Existing dated reports in this directory are separate audits and remain unchanged.

| Plan | Priority | Effort | Depends on | Status |
|---|---|---|---|---|
| [001 — Internal extension spine](001-internal-extension-spine.md) | P1 | L across phases | none | TODO |

Phase dependency: inventory -> permissions -> internal facts/policies -> commands -> narrow ports/example module -> public-surface ADR only. The small command-catalog registration fix may land independently before permissions.

## Findings considered and rejected

- ClientPorts are already a stable authoritative plugin surface: rejected; they are client output channels and contain internal/wire concepts.
- EventBus currently has a concurrent-subscribe bug exercised by production: not established; registrations are composition-time. Caller-thread semantics remain an extension constraint.
- GameRules.TrySet is a current data race: rejected; synchronization and snapshot publication are explicit. Future owner ordering requires a deliberate design choice.
- Every world mutation must immediately be routed through a universal protect/event framework: rejected; first proof explicitly covers player actions, with environmental protection deferred.

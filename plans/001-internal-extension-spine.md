# Plan 001: Establish an internal extension spine before any public plugin surface

## Status

- Priority: P1
- Category: architecture / correctness / authorization
- Effort: L across independently reviewed phases
- Risk: MED; HIGH for multiblock authorization and inventory transaction changes
- Confidence: HIGH for current-state findings; proposed contracts require implementation validation
- Depends on: none
- Planned at: `c73c0cf`, 2026-11-16
- Status: TODO. This is an audit handoff; no implementation or tests were run.

## Executor instructions and drift check

Read AGENTS.md, ARCHITECTURE.md and ADRs 21, 97 and 100 first. Preserve Gameplay != Protocol != Packets, the gameplay single writer, bounded handoffs and the deliberately frozen public plugin API. Registration below is internal composition only. Do not create a loader, discovery, public interfaces, or an event framework for packets.

Run `git diff --stat c73c0cf..HEAD -- src/zenith src/zenith.Tests docs/adr` and `git status --short`. If relevant paths changed, compare the symbols below against the live tree before implementing. Do not overwrite unrelated work. Make one logical change per commit, matching the current descriptive imperative commit style. Do not push or open a PR without instruction.

## Why this matters

Commands are already protocol independent and gameplay owns most mutation. However real command execution always supplies Operator, events expose live Player objects and run on the publisher's thread, and client channels are output ports rather than authoritative domain operations. An internal module cannot currently enforce protection safely without editing feature owners or receiving their mutable internals.

## Current state and evidence

- `src/zenith/Gameplay/Commands/CommandCatalog.cs:12`: registration, aliases and freeze; permission enum only Any/Operator. Register checks existing names but can partially mutate on duplicate aliases inside the new definition. Suggest does not filter permissions.
- `src/zenith/Gameplay/Commands/CommandRuntime.cs:85`: Execute parses with `CommandPermission.Operator`; gamemode/effect/diagnostics are registered Any. Runtime freezes its own catalog and dispatches through a central switch. Sample is feedback only, help ignores its optional argument, summon reports completion after enqueue.
- `src/zenith/Session/BedrockCommandAdapter.cs:15`: catalog-to-wire projection. Metadata currently has no principal and is sent during resource-pack join. Keep Bedrock details here.
- `src/zenith/Rulebook/GameRules.cs:76`: TrySet is a deliberately synchronized snapshot API. Do not report it as an existing data race; decide explicitly whether future authorized mutations move to the owner.
- `src/zenith/Gameplay/Entities/MobSpawner.cs:15`: unbounded ConcurrentQueue, request accepted outside tick and applied on owner. Bound it and carry authority/freshness context before expanding administrative commands.
- `src/zenith/Event/EventBus.cs:37`: synchronous caller-thread Action<T> dispatch, exception log and continue. Suitable for optional existing observers, unsuitable for fail-closed protection.
- `src/zenith/Event/PlayerEvents.cs`: events expose live Player; ProtocolNegotiateEvent is infrastructure, not a gameplay event.
- `src/zenith/Session/Handler/LoginSessionHandler.cs:259`: login event precedes resource packs/spawn. `Gameplay/Replication/PlayerPresenceAnnouncer.cs:15` announces world join here but suppresses pre-spawn quit. Actual world entry is `Session/ChunkStreamSystem.cs:120`, ApplySpawnReady.
- `src/zenith/Session/NetworkSession.cs:291`: close queues container cleanup/persistence; quit notification does not mean authoritative cleanup or disk save completed.
- `src/zenith/Gameplay/Ports/ClientPorts.cs`: output channels contain concrete Player/Inventory/ChestStore and wire correlation concepts. Never give these to the example module.
- `src/zenith/Player/Player.cs:70`: IdentityStable distinguishes ephemeral persistence identity, not authenticated trust. Player also exposes Session, Level, Windows and mutable state.
- `src/zenith/World/World.cs:452`: TrySetBlock checks height and writes/enqueues neighbors; no subject/cause/policy. GetBlock may hydrate, TryGetBlock only reads resident state.
- `src/zenith/Gameplay/WorldInteraction/BlockEditSystem.cs:158`: consumes placement stack before OnPlaced and primary/extra writes. Protection must run before the whole effect batch. CreeperSystem and BucketSystem independently call TrySetBlock; protection of player clicks alone is not immutable-region protection.
- `src/zenith/World/ChestStore.cs:54`: TryGetSlots returns the actual slot array. Do not expose it.
- `src/zenith/Gameplay/Inventory/InventorySystem.cs:224`: real client transaction validates replay, generation and advertised stack IDs, snapshots all sources, rolls back and persists/replicates after success. Do not fake a Bedrock inventory request for an internal module.
- `src/zenith/Gameplay/Survival/PlayerDamage.cs:36`: Conclude finishes chest release/death loot after ApplyCore. A post-death fact must follow the complete outcome, including explicit loot failure, not just health reaching zero.
- `src/zenith/Server/Composition/GameplayComposition.cs`: internal feature modules and registration order; not extension contracts. `ServerServices` is deliberately broad internal composition, not a module port.
- `src/zenith/Server/ZenithServer.cs:422`: coordinated shutdown awaits transports/loop, drains saves, settles gravity and flushes persistence. Timeout/failure is logged; Stopped does not imply durable flush success.

## Scope

Work in the cited gameplay owners, Player intents/state, command core, Session adapters, Server composition/config/lifecycle, relevant tests, and matching docs/ADRs. New internal permission/read-model/policy types belong in those domain folders. Do not edit networking transports, codecs, ECS internals or Cinnabar. Do not introduce a new public assembly or public extension namespace. Implement only the phase currently selected; phases are not permission for an unrelated refactor.

## Verification commands

The full command is the repository CI command from `.github/workflows/ci.yml`:

`dotnet test zenith.sln -c Release --nologo -m:1 -p:NodeReuse=false -p:BuildInParallel=false`

Focused iteration uses:

`dotnet test src/zenith.Tests/zenith.Tests.csproj -c Release --nologo -m:1 -p:NodeReuse=false -p:BuildInParallel=false`

Expected: exit 0, all tests pass. Establish the live baseline before implementation; no fresh baseline was claimed by this audit. Use filters while iterating and run the complete CI command before closing a phase.

## Phase 0 — Closed inventory

Objective: agree the factual baseline above and the limited proof scope.

Work: retain A/B/C classification in the audit report; distinguish output ports, integrity guards and thread-safe configuration APIs from extension authority. Record the prototype as protection of player place/break/container access, not environmental immutability.

Exit: every prototype mutation and denial point has a named owner; identity trust, event timing and shutdown flush semantics are explicit. Dependencies: none. Non-goals: implementing changes as part of the audit.

## Phase 1 — Minimal permissions

Work: add internal canonical principal plus trust/session freshness, exact permission nodes with defaults Everyone/Operator/Nobody, explicit grants/denies and precedence deny > explicit grant > default. Unknown nodes deny; no wildcard or groups. Bootstrap administrators from local config using canonical identity and trust policy. Do not persist privileges for ephemeral identities or equate offline UUID stability with authentication. Keep grants in a dedicated authority-owned store, not incidental PlayerData fields. Snapshot reads may cross threads; runtime writes use bounded handoffs.

Apply checks to all existing privileged commands, including self/others for gamemode/effect and gamerule read/write. Final checks occur before owner mutation, not only while parsing. Carry principal, operation ID and session generation through deferred operations. Unknown/stale targets and revoked authority must reject.

Exit tests: default player cannot invoke administrative commands even with forged wire input; bootstrap and revocation work across reload/reconnect; target replacement and revoke-before-commit deny without effects. Preserve existing command tests. Dependencies: phase 0. Non-goals: permission ecosystem, wildcard, public permission API or op command needed for bootstrap.

## Phase 2 — Core domain facts and policy decisions

Deliver as three separately reviewed increments. Never combine presence correction with multiblock planning in one PR.

### Phase 2a — Presence and immutable payloads

Work: move presence from admitted-login to owner-committed world entry; distinguish connection closed from owner-cleaned world departure. Use immutable IDs/value payloads and composition-only frozen subscriptions. Facts run after complete commit; follow-up mutations enqueue bounded work for a later tick. Keep network-thread residency cleanup explicitly internal. Observer exceptions log without changing committed outcomes. Amend ADR21's event limits without opening a public surface.

Exit tests: failed spawn produces no world join/leave; successful entry and owner-cleaned exit each publish once per entered session; observers receive no mutable domain references; observer failure cannot change the committed result. Dependencies: phase 0; may run alongside phase 1. Non-goals: block policies, cancellation or multiblock planning. Effort S-M / risk LOW.

### Phase 2b — Single-cell place/break policy

Work: typed synchronous Allow/Deny evaluation on the owner before any effect. Callbacks receive no mutable domain objects, perform no async work and cannot mutate recursively. Policy exceptions deny. Enumerate operations proven to affect a single cell; exclude coupled cells, block entities and container contents. Unsupported operations in the protected path must reject before effects until phase 2c, never bypass policy silently.

Exit tests: denied/policy-error edits preserve blocks, stack counts, metadata, drops and durability; no committed event on refusal; one fact after supported operation success; explicit rejection tests for operations outside this increment. Dependencies: phases 1 and 2a. Non-goals: full Protect proof, multiblock, environmental or container protection. Effort M / risk MED.

### Phase 2c — Pure batch planning and policy

Work: derive and validate the complete edit batch before consuming stacks, changing block entities, producing drops or writing any cell. Evaluate one immutable proposal and let the owner commit the complete result. Do not implement post-mutation cancellation.

Exit tests: planning never mutates; denial of any coupled cell preserves all resources; failed application leaves no partial commit; success emits one complete batch fact, without duplicate single-cell facts; phase 2b tests stay green. Dependencies: phase 2b. Non-goals: environmental protection, converting all systems to events or raw packet listeners. Effort L / risk HIGH, concentrated in this increment rather than phase 2a.

## Phase 3 — Command contribution and operational commands

Work: validate registration atomically including duplicate aliases within a definition; bind internal definition to executor in composition before freeze, eliminating the need to edit the central switch for the example module. Define internal sender/context and typed Rejected/Accepted/Applied results. General UI rule: enqueue acceptance never means application; announce completion only after the owner confirms, and correlate later failure with its operation. Apply this to every deferred operation, including summon. Keep targets as IDs/constraints rather than mutable Player values across handoff. Filter help/suggestions/Bedrock metadata using the same policy and refresh on permission changes; retain wire details in adapter. Correct summon feedback/queue capacity. Help argument/sample placeholder cleanup is a separate small hygiene PR parallel to phase 1; it must not block permissions, phase 2 or phase 3, or expose diagnostics before authorization.

Priorities: secure existing commands first; add list/status for operations; add save/stop only through lifecycle coordination if still needed. Defer give/clear until inventory-domain transaction port and tp until owner teleport semantics exist. Do not add selectors/RCON/discovery.

Exit tests: adding a composed internal test command needs no runtime-switch edit; duplicate registration leaves catalog unchanged; freeze prevents later changes; both command wire entrances enforce the same authority; metadata decodes to EOF; queue full/stale/revoked requests report rejection rather than success. Dependencies: phases 1–2. Non-goals: complete vanilla catalog, public command hooks.

## Phase 4 — Narrow ports and one internal protect example

Work: provide immutable player/world/block query models, explicit residency result, command invocation context and owner-side action decisions. Freeze only these internal semantics and error/freshness/ownership rules. Keep concrete Player, World, Windows, PlayerInventory, ClientPorts and ServerServices private to composition/owners. Example Protect contributes one permissioned configuration command and read-only policy for complete player edit batches and container access. Core performs validation, state change and replication.

Protect is the chosen first proof; it needs no actor surface or ECS exposure. Document environmental edits as outside this prototype. Cover denied inventory transfers after protection changes or container permissions are revoked; preventing only a new open is insufficient. Economy is deferred to a later track with a persistent integer ledger and authoritative balance/transfer operations; an in-memory number is not a sufficient proof. Item purchases require a real cross-resource transaction and cannot be built by snapshotting bag alone. Economy does not replace the first Protect proof.

Inventory future port: immutable stack snapshots plus exact grant/remove/transfer requests with bounded acceptance, explicit failure and committed result; no client request IDs/net IDs/window generation in module contracts. Actor surface is deferred unless an actual consumer appears.

Exit tests: headless protect works without Packet/Session/Protocol/ClientPorts/ECS references or mutable domain concrete types; boundary tests enforce those exclusions without expanding Player setter allowlists. All declared protected actions deny before effects, including coupled cells and already-open container transfer. Conservation/replay/reload tests remain green. Dependencies: phases 1–3. Non-goals: immutable regions across fire/fluid/explosion/gravity, shops, universal actor facade.

## Phase 5 — Public-surface ADR only

Work: document lessons from the internal module: capability boundaries, trust/identity, compatibility, error isolation and costs. Define internal lifecycle stages: resources ready -> register/freeze -> running; stopping rejects new work -> owner drain/settle -> persistence outcome -> stopped. No callback may mutate after quiescence; report flush success separately from teardown completion.

Exit: reviewed ADR cites the passing module contract tests and unresolved external isolation risks; explicitly states loader remains unimplemented. Dependencies: phase 4. Non-goals: external assemblies, discovery, hot reload, sandbox claims, scheduler/DI framework.

## First three small commits

Parallel hygiene PR: fix help's ignored argument and sample's placeholder semantics without widening diagnostics access. This is independent of permission-model work and must not become a prerequisite for it.

1. `Validate command registration before mutating the catalog`: atomic alias/name validation, snapshot/copy definition collections as needed, freeze/duplicate regression tests. No command expansion. S / LOW.
2. `Add internal permission principals and explicit node defaults`: pure evaluator + bootstrap trust configuration/storage policy and tests; keep it internal. M / MED.
3. `Enforce permissions for existing command invocations`: replace forced Operator, classify every built-in, authorize both input paths, carry/recheck authority for deferred effects and filter discovery. If this exceeds a reviewable change, split by command family without leaving any previously Any administrative command unintentionally open. M / MED.

## Test plan and maintenance

Follow CommandCatalogTests, CommandRuntimeExecuteTests, BedrockCommandAdapterTests, ChatAndGameModeIntentTests, PlayerPresenceAnnouncerTests and ZenithServerLifecycleTests for existing test patterns. Retain ArchitectureBoundaryTests, InventorySystemIntentTests, InventoryConservationTests and DeathLootAtomicityTests. New permission/policy tests must assert both result and unchanged authoritative resources on refusal. Do not add tests that merely mirror implementation.

Do not treat the architecture setter test as proof that arbitrary inventory/world mutation is impossible: it checks a narrower boundary. Add a dedicated dependency boundary for the example module. Do not call health=0 a completed death or a queued spawn a successful spawn. Maintain denial reasons, ordering and budgets as contracts before external exposure.

## STOP conditions

- Relevant source no longer matches the evidence: refresh the audit for those symbols before implementation.
- Required changes cross into ECS, packet codecs, network transport or public loader: report the new dependency; do not widen scope.
- Protect cannot derive the complete effect batch without mutating: stop that integration until the owner has a pure preflight plan; never fall back to post-mutation cancellation.
- A verification gate fails twice after a focused correction: report failure and preserve reproducible evidence.
- No canonical trusted identity can be established for a configured persistent administrator: reject that config; do not silently trust a username/offline UUID.

At completion of a selected phase, update this plan and its index. Leave later phases TODO until their tests and exit criteria pass.

## Status log

| Phase | Status | Evidence / gaps |
|-------|--------|-----------------|
| 0 Permissions | Done | `Principal`, `PermissionRegistry/Policy/Authority`, trust model (Authenticated/SelfAsserted/Ephemeral), config `permissions.*`, owner-side recheck via `AuthorityGate`. Gap: runtime grant persistence (`/op`), command metadata not refreshed on permission change. |
| 1 Domain policy and events | Done for block edits and container access | `IBlockEditPolicy` (plan, judge, commit; whole coupled edit judged first), `BlocksChanged` fact, `IContainerAccessPolicy` (open + every transfer; a withdrawn right closes an open window), presence facts (`PlayerEnteredWorld`/`PlayerLeftWorld`). Gaps: door toggles, bucket, creeper, fluid, fire and gravity writers do not pass block policy yet. |
| 2 Commands | Mostly done | Permission-gated catalog; `CommandBinding` (grammar + handler registered together before freeze, clashes fail composition); handlers get values-only `CommandInvocation` and `PlayerRef` targets; `CommandOutcome` Rejected/Accepted/Applied. TODO: bounded summon queue and correlated results, command-metadata refresh on permission change. |
| 3 Internal Protect module | TODO | Needs the TODO above plus a dependency-boundary test. Policies for blocks and containers are ready to host it. |
| 4 Re-audit / ADR | TODO | Only after Protect exists. |

Unverified: nothing here has been exercised with a real Bedrock client; evidence is the headless suite (1805 passing).

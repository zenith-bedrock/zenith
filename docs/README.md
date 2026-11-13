# Zenith documentation

Everything here lives in-repo and is reviewed via PR, not on the GitHub Wiki — Zenith's doc culture is ADR-first and commit-backed (see [`adr/`](adr/README.md)), which needs version control tied to the code it describes. Think of this page as the wiki sidebar: pick the section that matches what you're trying to do.

Day-to-day engineering constraints live in the root [`ARCHITECTURE.md`](../ARCHITECTURE.md) (living rules, not narrative).

---

## Start here

New to the repo? Read in this order: root [`readme.md`](../readme.md) → [`architecture.md`](architecture.md) → [`ARCHITECTURE.md`](../ARCHITECTURE.md) → [`CONTRIBUTING.md`](../CONTRIBUTING.md). `AGENTS.md` is an additional guide for people using coding agents or automation.

## How-to guides

Task-oriented — you have a specific thing to do.

| Guide | Use for |
|-------|---------|
| [Developer experience](dx.md) | Contributor workflow, hot-path conventions, local reference clones |
| [Vanilla behavior guide](vanilla-behavior.md) | Testing in-game: what should match vanilla, what to report as a bug |
| [Alpha gate](alpha-gate.md) | `v0.0.1-alpha` tag checklist (compose / test / non-goals) |
| [Protocol churn](protocol-churn.md) | Checklist for bumping to a new Bedrock client protocol (ADR §22) |
| [Protocol import](protocol-import.md) | `protocol-import` tool: pull/scaffold/diff Mojang packet schemas |
| [Runtime and behavioral validation](runtime-validation.md) | Load baseline and real-client differential-smoke workflow |
| [Runtime diagnostics](diagnostics.md) | Diagnostics library, snapshots, instrumentation and overhead checks |
| [Disconnect lifecycle](disconnect-lifecycle.md) | Session/gameplay teardown, persistence and event semantics |
| [`CONTRIBUTING.md`](../CONTRIBUTING.md) | Issue / PR norms |
| [Release notes template](release-notes-template.md) | Notes body for GitHub Releases |

## Runtime architecture

Explains the current, concrete shape of the game runtime — read these before touching gameplay code.

| Doc | Covers |
|-----|--------|
| [ECS runtime](ecs.md) | The real ECS in `src/zenith/Ecs/` — `EntityId`/generation, component storage, query semantics, structural mutation, runtime-id mapping, migration model (Zombie/Minecart/Projectile), what's deliberately not built (archetypes, scheduler, plugin API) |
| [Entity fidelity](entity-fidelity.md) | Entity yaw/pitch, movement fidelity findings and known gaps |
| [Entity fidelity validation](entity-fidelity-validation.md) | How entity fidelity is validated against a real client |
| [Entity behavior matrix](entity-behavior-matrix.md) | Per-mob behavior coverage vs vanilla |
| [World generation](world-generation.md) | Overworld generation pipeline |
| [NetherNet](nethernet.md) | Independent WebRTC/NetherNet transport: signaling, identity, framing (ADR §140) |
| [Entity catalog](entities.md) | Every runtime/gameplay category (Player, mobs, vehicles, projectiles, drops), what's ECS-backed vs. the OOP `IDamageableActor` pattern, capability matrix, and the evidence trail for what's shared vs. only looks similar |

## Reference

Lookup-oriented — you know what you need, you just need the exact value or rule.

| Reference | Covers |
|-----------|--------|
| [Technical reference](technical-reference.md) | Protocol version SSOT, embedded assets, external study repos, owned vs borrowed |
| [Architecture](architecture.md) | Layers, data flow, project graph, freeze list |
| [Naming conventions](naming.md) | Verb/prefix vocabulary by layer (`Handle*`, `Submit*`, …) |
| [`ARCHITECTURE.md`](../ARCHITECTURE.md) | Full constraint list, GameLoop rules, smoke manual, roadmap phases |
| [`AGENTS.md`](../AGENTS.md) | Additional guardrails for coding agents and automation |
| [`schemas/zenith.schema.json`](../schemas/zenith.schema.json) | Config IDE autocomplete (boot validation is `ServerConfig.Validate()`) |

## Explanation

Understanding-oriented — the *why* behind a choice, not the mechanics.

| Doc | Covers |
|-----|--------|
| [Architecture decision records](adr/README.md) | ADR §1 onward, one file per decision — why we chose X. [Timeline and non-goals](decisions.md) |
| [Comparison](comparison.md) | Zenith vs PocketMine, NukkitX, BDS, and other stacks |
| [Why Zenith / future](why-zenith.md) | Positioning, honesty about maturity, long-term bet |
| [Robustness / DX debt](robustness-dx-debt.md) | Platform-health risks tracked apart from product roadmap (ADR §54) |
| [Architecture and DX audit](audit-2026-11-13-architecture-and-dx.md) | Findings after the `Player` split, with done/open status |
| [.NET opportunities](dotnet-opportunities.md) | Platform features not used yet, expected benefit, what is done (debug logging) and what needs measuring |

## Planning

Where the project is headed — check before picking up work.

| Doc | Covers |
|-----|--------|
| [Roadmap](roadmap.md) | Current dependency graph, maturity gates and NOW/NEXT/LATER |

## Historical evidence

How Zenith arrived at its current architecture — phase-by-phase findings and point-in-time audits.
Not required reading to work on the current codebase; the canonical docs above already carry the
durable conclusions. Read these when you need the underlying measurements, rejected alternatives,
or the full reasoning behind a specific line in the [decision records](adr/README.md).

| Doc | Covers |
|-----|--------|
| [Phase index](history/phases/README.md) | Every phase report, one line each: purpose, key result, current relevance, canonical successor |
| [`history/audits/`](history/audits/) | Point-in-time audits — ECS feasibility spike, repo/package topology, maturity assessment |
| [`adr/`](adr/README.md) | One file per architecture decision (`ADR §N`); the three long-form designs sit beside their records |

---

Library-specific notes:

- [`libs/leveldb/README.md`](../libs/leveldb/README.md) — `Zenith.LevelDB` API and LSM backlog
- Root [`readme.md`](../readme.md) — project one-pager

**Status:** early development. Prefer these docs over marketing claims; if code and docs disagree, code + `ARCHITECTURE.md` win.

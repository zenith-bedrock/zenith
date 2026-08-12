# Zenith documentation

Everything here lives in-repo and is reviewed via PR, not on the GitHub Wiki — Zenith's doc culture is ADR-first and commit-backed (see [`decisions.md`](decisions.md)), which needs version control tied to the code it describes. Think of this page as the wiki sidebar: pick the section that matches what you're trying to do.

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
| [Runtime and behavioral validation](runtime-validation.md) | Load baseline and real-client differential-smoke workflow |
| [Runtime diagnostics](diagnostics.md) | Diagnostics library, snapshots, instrumentation and overhead checks |
| [`CONTRIBUTING.md`](../CONTRIBUTING.md) | Issue / PR norms |
| [Release notes template](release-notes-template.md) | Notes body for GitHub Releases |

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
| [Decision history](decisions.md) | ADR §1 onward — why we chose X, traced from commits and milestones |
| [Comparison](comparison.md) | Zenith vs PocketMine, NukkitX, BDS, and other stacks |
| [Why Zenith / future](why-zenith.md) | Positioning, honesty about maturity, long-term bet |
| [Robustness / DX debt](robustness-dx-debt.md) | Platform-health risks tracked apart from product roadmap (ADR §54) |

## Planning

Where the project is headed — check before picking up work.

| Doc | Covers |
|-----|--------|
| [Roadmap](roadmap.md) | Current dependency graph, maturity gates and NOW/NEXT/LATER |
| [Maturity & ECS readiness audit](audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md) | Capability evidence, mature-server comparison and ECS decision gate |

---

Library-specific notes:

- [`libs/leveldb/README.md`](../libs/leveldb/README.md) — `Zenith.LevelDB` API and LSM backlog
- Root [`readme.md`](../readme.md) — project one-pager

**Status:** early development. Prefer these docs over marketing claims; if code and docs disagree, code + `ARCHITECTURE.md` win.

# Zenith documentation

Narrative docs for architecture, history, comparisons, and developer experience.
Day-to-day engineering constraints live in the root [`ARCHITECTURE.md`](../ARCHITECTURE.md) (living rules).

| Doc | What it covers |
|-----|----------------|
| [Architecture](architecture.md) | Layers, data flow, project graph, freeze list |
| [Decision history](decisions.md) | Why we chose X — traced from commits and milestones |
| [Protocol churn](protocol-churn.md) | Human checklist vs Bedrock client bumps (ADR §22) |
| [Alpha gate](alpha-gate.md) | `v0.0.1-alpha` tag checklist (compose / test / non-goals) |
| [Vanilla behavior guide](vanilla-behavior.md) | Tester-facing: what should match vanilla, what's simplified, what's not implemented yet |
| [Roadmap](roadmap.md) | H1 closed; **Yes-next** order (not PM parity) |
| [Release notes template](release-notes-template.md) | Notes body for GitHub Releases |
| [Comparison](comparison.md) | Zenith vs PocketMine, NukkitX, BDS, and other stacks |
| [Developer experience](dx.md) | What you get day-to-day as a contributor or extension author |
| [Why Zenith / future](why-zenith.md) | Positioning, honesty about maturity, long-term bet |
| [`CONTRIBUTING.md`](../CONTRIBUTING.md) | Issue / PR norms |
| [`AGENTS.md`](../AGENTS.md) | Folder layout for humans and agents |
| [Technical reference](technical-reference.md) | SSOT for protocol, ADR § tags, external study repos, embedded assets |

Library-specific notes:

- [`libs/leveldb/README.md`](../libs/leveldb/README.md) — `Zenith.LevelDB` API and LSM backlog
- Root [`readme.md`](../readme.md) — project one-pager

**Status:** early development. Prefer these docs over marketing claims; if code and docs disagree, code + `ARCHITECTURE.md` win.

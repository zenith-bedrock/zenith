# Zenith documentation

Narrative docs for architecture, history, comparisons, and developer experience.
Day-to-day engineering constraints live in the root [`ARCHITECTURE.md`](../ARCHITECTURE.md) (living rules).

| Doc | What it covers |
|-----|----------------|
| [Architecture](architecture.md) | Layers, data flow, project graph, freeze list |
| [Decision history](decisions.md) | Why we chose X — traced from commits and milestones |
| [Comparison](comparison.md) | Zenith vs PocketMine, NukkitX, BDS, and other stacks |
| [Developer experience](dx.md) | What you get day-to-day as a contributor or extension author |
| [Why Zenith / future](why-zenith.md) | Positioning, honesty about maturity, long-term bet |

Library-specific notes:

- [`libs/leveldb/README.md`](../libs/leveldb/README.md) — `Zenith.LevelDB` API and LSM backlog
- Root [`readme.md`](../readme.md) — project one-pager

**Status:** early development. Prefer these docs over marketing claims; if code and docs disagree, code + `ARCHITECTURE.md` win.

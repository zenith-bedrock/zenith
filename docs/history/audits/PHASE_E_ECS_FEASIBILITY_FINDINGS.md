# Phase E — ECS feasibility findings

> **Historical.** This was the first ECS feasibility spike and found no material gain, leading to
> the deferral recorded in `decisions.md` §102. That conclusion is still accurate as a record of
> what was measured then. Phase XXI later made an explicit scope decision — not a new measurement —
> to build a real ECS for a small slice anyway; see [`decisions.md` §106](../../decisions.md) and
> [`ecs.md`](../../ecs.md).

## Hypothesis

An internal, single-writer archetype/SoA representation might materially reduce the simulation,
lifecycle allocation, or repeated actor plumbing of Zenith world actors. It is not a proposal to
move players, sessions, Protocol, Packets, RakNet, inventory, persistence, commands, visibility or
plugins into an ECS.

## Control and prototype

The control remains the committed direct production model: concrete `ProjectileStore`,
`ZombieStore`, `FallingBlockStore` and the sparse, merge-specific `FloorDropStore`, each ticked by
its owning GameLoop system. Phase-D's `actor-churn` harness continues to be the only benchmark that
uses the real Protocol/RakNet projection path.

`src/zenith.Benchmarks/EcsFeasibilityHarness.cs` is an isolated experiment. It has a direct-list
control and three contiguous SoA columns: moving/lifetime (Projectile/FallingBlock-like), health
(Zombie-like), and lifetime-only (FloorDrop-like). Both have deterministic order, the same
100-tick warmup, GC reset, 400 measured ticks, expiry/death removal and replenishment. It has no
production-runtime reference beyond the benchmark project reference and no public API.

## Workloads and results

The initial mixed workload uses a 1:1:1:1 sequence of Projectile, Zombie, FallingBlock and
FloorDrop-like entries. Movement updates position/velocity/gravity/age; Zombies take periodic
damage and die; projectile/drop lifetimes expire; removed actors are replenished. This establishes
simulation plus create/remove churn at 100 and 1,000 actors. The reproducible command is:

```powershell
dotnet build src/zenith.Benchmarks/zenith.Benchmarks.csproj --no-restore -p:UseSharedCompilation=false
dotnet run --no-build --project src/zenith.Benchmarks -- --ecs-spike 100,1000
```

On the Phase-E workstation, direct simulation was about 0.002/0.021 ms per tick at 100/1,000
actors; the SoA experiment was about 0.003/0.028 ms. Both allocated 8–9 B/tick with no GC. This
is a small synthetic experiment, not a capacity claim and not an ECS acceptance signal.

The harness also has a ten-observer **neutral projection traversal**. It deliberately does not
construct Bedrock packets: the real wire baseline remains Phase D. The traversal itself dominates
the small simulation figures, consistent with Phase D; it does not measure packet creation,
datagrams or egress, therefore cannot support a networking conclusion.

## Query, identity and structural-change limits

The experiment proves compact swap removal and replenishment for the three concrete columns. It
does **not** yet prove generation-safe internal handles/row maps, an equivalent Projectile→Zombie
target lookup, or a component/archetype transition. A transition is not present in the current
world-actor lifecycle, so inventing one would not be fair; generation-safe identity and the real
cross-type query are required before accepting a runtime representation.

## DX and complexity

The direct control maps one actor to its state/store/system plus explicit protocol projection. The
prototype already needs three column implementations and explicit compact-removal rules; it has
not reduced the production registration/projection plumbing because it is intentionally isolated.
It therefore demonstrates no DX win. Source generation, component registration, query API,
reflection and plugin surface were deliberately not introduced to hide that cost.

## Mature storage-model comparison

This spike does not treat archetypes as a default answer. Unity Entities documents archetypes as
component-set chunks with packed swap removal, and explicitly cautions that frequent component
add/remove moves entities and is resource-intensive ([Unity archetypes](https://docs.unity.cn/Packages/com.unity.entities%401.0/manual/concepts-archetypes.html)). Flecs likewise documents sparse components as a trade: stable/out-of-table component storage makes add/remove cheaper but queries slower ([Flecs component traits](https://www.flecs.dev/flecs/md_docs_2ComponentTraits.html)). Bevy exposes the same choice directly: tables favor cache-friendly iteration, sparse sets favor component churn ([Bevy ECS storage](https://docs.rs/bevy/latest/bevy/ecs/index.html)). EnTT demonstrates the pool alternative: each component pool is a packed sparse set, with versioned/recycled entity identifiers ([EnTT ECS](https://github.com/skypjack/entt/wiki/Entity-Component-System)).

For Zenith's current actors, position/velocity/lifetime loops are stable while Projectile spawn and
removal churn; there is no evidenced add/remove-component lifecycle. The better next experiment is
therefore a **hybrid**, not a full archetype runtime: retain concrete actor lifecycle/identity and
test a narrow contiguous pool only for the proven moving/lifetime hot path, with generation-safe
handles and the actual Projectile→Zombie lookup. Do not choose a full archetype ECS now: it adds
row-map/migration complexity without a demonstrated composition-query or DX benefit. Do not choose
generic sparse component pools either: they would add an unproven registry/query surface and trade
away the current linear iteration before its bottleneck is isolated.

## What ECS does not solve

Phase D's 1,000-actor/10-observer tail is dominated by global projection, packet encoding and
transport fan-out. An archetype layout does not choose observer interest, chunk knowledge, packet
shape, batching or transport policy. It also does not authorize jobs, worker threads or a
VisibilitySystem.

## Falsification analysis and decision

**Decision: DEFER ECS — insufficient comparable evidence.** The first SoA result is not materially
faster than the direct control at the measured scales, carries more lifecycle implementation
complexity, and leaves the decisive cross-type query and identity semantics unmeasured. The real
observed production bottleneck remains replication fan-out, which ECS does not solve.

Reopen only with a revised isolated experiment that adds the same Projectile→Zombie lookup and
generation-safe compact row mapping to both controls, reports separate create/remove throughput,
and compares identical packet construction/projection without assigning visibility policy to ECS.
Until then retain the direct model and pursue actor-interest/AI/gameplay work independently.

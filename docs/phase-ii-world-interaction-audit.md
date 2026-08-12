# Phase II — World interaction and survival loop audit

## Scope and starting point

Phase II does not rebuild the Phase-I primitives. `StackId`, `PlayerInventory`,
`FloorDropStore`/`FloorDropSystem`, persistent inventory/chests, `BlockEditSystem` and the
protocol projections already form the small survival loop. The work here exercised their
authoritative transition together and closed two concrete gaps found in that exercise.

The ownership remains unchanged:

```text
Client proposes → handler bounds an intent → GameLoop gameplay decides
Gameplay → Protocol → Packets → RakNet
Diagnostics observes the completed work
```

## Existing primitives that were sufficient

| Concern | Existing primitive | Phase-II use |
|---|---|---|
| Block mutation / replication | `World.TrySetBlock` + `BlockEditSystem` | authoritative place/break and `UpdateBlock` fan-out |
| Item identity and inventory commit | `StackId`, `PlayerInventory`, `InventorySnapshot` | exact selected-stack validation and rollback-safe consume |
| Drop lifecycle / replication | `FloorDropFanout`, `FloorDropStore`, `FloorDropSystem` | plan, publish, delayed pickup and remove |
| Container contents | `ChestStore` | preflight content plus chest-item destination before removal |
| Tool pressure | `DigProfiles` + `Tools` | stone needs the existing wooden-pickaxe capability; durability remains unneeded |
| Durability | LevelDB overlay / `inv:` / `ct:` keys | world edits, player inventory and chests persist; floor drops remain RAM-only |

No loot table, item hierarchy, transaction framework, generic container, ECS or actor abstraction
was introduced.

## Implemented conservation rules

### Placement

`InGameUseItemHandler` snapshots the exact `StackId` in the bounded `BlockEditIntent`. Before
consumption, `BlockEditSystem` verifies that the hotbar slot still contains that stack. A queued
inventory rearrangement therefore cannot pay for a stone placement with a different block that
arrived in the same slot. The pre-consume inventory snapshot is restored if the world write or
concrete chest-store creation fails.

### Breaking and drops

For ordinary concrete block drops, the system now checks the two valid destinations before it
turns the block into air: inventory first, then `FloorDropFanout.CanDeposit`. If both are full,
the break is rejected and the client receives the existing block resync.

Chest destruction uses the existing `FloorDropFanout` batch plan. It simulates the concrete chest
contents and chest item against the inventory, plans every surplus floor cell at once, and only
then removes the chest. The actual surplus is published with the same batch primitive. A bounded
floor store consequently cannot leave a removed chest with dropped or silently lost contents.

Focused contracts cover stale placement identity, full inventory + full floor store for a stone
break, and the same no-loss condition for a chest with contents. Existing replay, reach, timing,
creative and competing-break contracts remain applicable.

## Persistence semantics

| State | Policy |
|---|---|
| Block overlays | durable |
| Player inventory | durable |
| Chest contents | durable |
| Floor drops | deliberately RAM-only |

The restart proof remains the existing LevelDB place/restart/reconnect smoke: it retains an
overlay, inventory count and chest contents. Persisting a transient floor item is a separate
lifecycle/storage decision and is not implied by this loop.

## External smoke evidence

Against a fresh local Debug server, `bun run smoke:place` observed the authoritative
`update_block` and `bun run smoke:break` observed the resulting air update. The existing
LevelDB `smoke:persist` remains the restart proof above.

`bun run smoke:floor-pickup` was also rerun and again could not establish the synthetic full-bag
precondition or observe `take_item_entity`. The concrete cause is the bot's nearest-available
2168 schema: its decoder does not emit the 2169 Cereal-shaped `ItemStackResponse` used by Zenith,
so the harness cannot prove its creative inventory requests or free a slot for pickup. This is a
known external-harness/protocol-schema limitation, not evidence that the gameplay transition
failed: the authoritative pickup path is covered by focused multiplayer/unit contracts and the
controlled runtime scenario uses it. It remains a smoke-bot follow-up; no production rule was
changed to make that synthetic packet observation pass.

## Reference comparison

The local Dragonfly source was consulted for behavior only. Its player path separates a bounded
break action from use-on-block and its block code distinguishes removal with drops from removal
without drops; its item entities also retain explicit pickup delay. Zenith already has the useful
behavioral equivalents: intent/tick ownership, a concrete break/drop decision, and delayed
`FloorDropSystem` pickup. Dragonfly's broad block/item interfaces, transactions and entity model
are not adopted.

BetterAltay remains a useful counterexample: its extensible item/event/object surface is much
broader than this server needs. Zenith keeps the direct `StackId`/capability-map representation.

## Controlled runtime sample

Command:

```powershell
dotnet run --no-build --project src/zenith.Benchmarks -- --runtime-load --world-interaction --players 10 --ticks 200
```

`world-loop` uses production GameLoop ordering. Ten isolated Survival players alternately place
and break a stone cell with the existing pickaxe rule; every twentieth tick it creates a concrete
dirt floor drop at each player for the production delayed pickup path. It verifies the conserved
stone stack and collected dirt after the run. It measures tick wall time, allocation/GC and real
protocol/RakNet output, not inbound packet decoding or live-client capacity.

One Debug in-process sample on this workspace:

| players | ticks | avg | p95 | allocation/tick | GC | datagrams | bytes |
|---:|---:|---:|---:|---:|---|---:|---:|
| 10 | 200 | 1.252 ms | 2.242 ms | 350,755 B | 8 / 1 / 0 | 3,090 | 2,854,542 |

The result is a characterization only. Its allocation level makes the scenario useful as a future
regression baseline, not a production capacity claim. The benchmark composes the same fixed-layout
Diagnostics timing shape and recorded: whole tick **1.206 ms**, `block-edit` **0.810 ms**,
`floor-drop` **0.063 ms**, and `inventory` **0.004 ms** average. Production diagnostics already
exposes those system names plus allocation, GC, packet-byte and datagram facts; no speculative
per-item metric was added.

## Pressure observed and deliberately deferred

The real pressure was not an absent framework. It was destination planning around a bounded
store and the identity gap between handler validation and tick consumption. Existing snapshots and
the existing floor-drop batch planner were sufficient to make those transitions explicit.

`FloorDrop`, Zombie, Skeleton and Projectile still share lifecycle/replication concerns but do
not justify a generic actor hierarchy. Block rules are still a small concrete set, and one
pickaxe only proves capability-map pressure — not durability, attributes or a full item runtime.
The next highest-value gameplay gap after this closure is a separate, concrete world behavior
slice, such as simple mob behavior or a narrowly evidenced block rule, rather than a framework.

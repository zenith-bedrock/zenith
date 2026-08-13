# Phase XIV.1 — Ground Mob Combat Consolidation

## Step 1 — Revalidated boundary

Re-inspected `Zombie.cs`, `Skeleton.cs`, `Cow.cs`, `ZombieSystem.cs`, `SkeletonSystem.cs`,
`CowSystem.cs` before changing anything, confirming Phase XIV's findings against the actual code
rather than the summary of it.

**Confirmed shared, byte-for-byte identical in structure across all three systems:**

- `ApplyPlayerAttacks` — reach check + `TryConsumeAttackIntent` + call into `TryApplyDamage`.
- `TryApplyDamage` — guard against an unstorable lethal hit → apply damage → broadcast health to
  already-replicated peers → on death: deposit loot, credit XP via `MobKillReward`, broadcast
  removal, remove from store.
- `CanDropLoot` — a single `FloorDropFanout.CanDeposit` call, one line, identical shape ×3.
- Entity shape — `EntityId`, `RuntimeId`, `PositionX/Y/Z`, `Health`, `IsActive`, `ApplyDamage`,
  `Remove()` are the same seven members on `Zombie`, `Skeleton`, and `Cow`.

**Confirmed still genuinely different, correctly left alone:**

- **Zombie** — `FindOrAcquireTarget`/`IsTargetValid`/`AdvanceTowardTarget`/`TryAttackPlayer`: nearest-
  player targeting, chase movement, retaliation against the player.
- **Skeleton** — `FindTarget`/`TrySpawnFromActor` shot logic: stationary, range-gated, fires
  projectiles instead of touching a player directly.
- **Cow** — `Wander`/`PickNewHeading`: no player awareness at all, periodic random heading.
- **Movement collision probe (`TryMove`)** — identical between Zombie and Cow, but Skeleton has no
  equivalent (it never moves). Only 2 of 3 instances — below the "three systems" threshold the rest
  of this consolidation used. **Left concrete, not extracted.**
- **Batched move replication (`ReplicateMoves`, `ProjectedPosition`)** — same 2-of-3 situation as
  `TryMove`, for the same reason (Skeleton doesn't move, so has nothing to batch-replicate).
  **Left concrete.**

This matches the brief's own instruction precisely: the extraction below touches only the
damage/loot/XP/removal bookkeeping. Targeting, movement, and attack decisions were not touched.

---

## Step 2 — What was implemented

`Gameplay/GroundMobCombat.cs`, two types:

```csharp
interface IGroundMob
{
    long EntityId { get; }
    ulong RuntimeId { get; }
    float PositionX { get; }
    float PositionY { get; }
    float PositionZ { get; }
    HealthState Health { get; }
    bool IsActive { get; }
    DamageResult ApplyDamage(DamageSource source, float amount);
    void Remove();
}

static class GroundMobCombat
{
    public static void ApplyPlayerMeleeAttacks<TMob>(...) where TMob : IGroundMob;
    public static bool TryApplyDamage<TMob>(...) where TMob : IGroundMob;
}
```

`IGroundMob` has exactly the seven members every mob already exposed — no `Update`, `Move`,
`Attack`, or `Think` was added, and nothing implements it through inheritance; `Zombie : IGroundMob`,
`Skeleton : IGroundMob`, `Cow : IGroundMob` are additive interface declarations over already-existing
members. `CanDropLoot` was not given its own method — it's folded into `TryApplyDamage`'s lethal-hit
guard, exactly where all three copies already used it, so there was nothing left to call it
separately.

`GroundMobCombat.TryApplyDamage` owns exactly what the brief listed — apply damage, health
replication, death detection, loot deposit, XP reward, actor removal, death replication — and
nothing else. The two things that still differ per mob (which store to remove from, and whether
extra per-peer state like a move-projection cache needs clearing on death) are passed in as two
small delegates (`removeFromStore`, `onDeathReplicatedToPeer`), not reimplemented per system.

Each system's own `TryApplyDamage`/`ApplyPlayerAttacks` became one-line wrappers:

```csharp
// ZombieSystem.cs
private void ApplyPlayerAttacks(Zombie zombie, IReadOnlyList<Player.Player> online) =>
    GroundMobCombat.ApplyPlayerMeleeAttacks(zombie, online, AttackDistance, AttackDamage, TryApplyDamage);

public bool TryApplyDamage(Zombie zombie, DamageSource source, float amount, IReadOnlyList<Player.Player> online) =>
    GroundMobCombat.TryApplyDamage(
        zombie, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Zombie",
        removeFromStore: _zombies.Remove,
        onDeathReplicatedToPeer: peer =>
        {
            _lastProjected.Remove((zombie.EntityId, peer.RuntimeId));
            ReplicatedRemovalCount++;
        });
```

`SkeletonSystem`'s version omits `onDeathReplicatedToPeer` entirely (it has no `_lastProjected`/
removal counter to clean up) — the delegate is optional (`Action<Player.Player>? = null`) precisely
so a mob with less bookkeeping doesn't have to fake having more.

---

## Explicitly not created

No `MobBase`, `LivingActor`, `EntityBase`, `CombatFramework`, `DamagePipeline`, component system, or
ECS. `IGroundMob` carries no behavior and nothing inherits from anything. `GroundMobCombat` is a
static helper with two methods, not a service, not registered anywhere, not injected — each system
calls it directly like a library function.

---

## Step 3 — Readability check

Line counts, before → after this extraction only (Cow's own numbers already reflect Phase XIV, not
this step):

| File | Before XIV.1 | After XIV.1 |
|---|---|---|
| `ZombieSystem.cs` | 307 | 256 |
| `SkeletonSystem.cs` | 143 | 105 |
| `CowSystem.cs` | 254 | 210 |

133 lines of byte-identical bookkeeping removed across the three files combined, replaced by one
~120-line shared helper used three times.

Opening each file now:

- **`ZombieSystem.cs`** — the visible, non-delegated methods are `EnsureBootstrapZombie`,
  `FindOrAcquireTarget`, `IsTargetValid`, `AdvanceTowardTarget`, `TryAttackPlayer`, `TryMove`,
  `ReplicateMoves`. Every one of them is about chasing, targeting, or moving. Combat bookkeeping is
  two one-line delegations. **Reads as a hostile chasing mob.**
- **`SkeletonSystem.cs`** — `FindTarget`, the shot-firing block in `Tick` (range check, cooldown,
  `_projectiles.TrySpawnFromActor`). **Reads as a stationary ranged mob.**
- **`CowSystem.cs`** — `EnsureBootstrapCow`, `Wander`, `PickNewHeading`, `TryMove`,
  `ReplicateMoves`. No targeting code exists at all — there is nothing to accidentally read as
  aggressive. **Reads as a wandering, passive mob.**

---

## Step 4 — Validation

`dotnet build zenith.sln` — clean, 0 warnings introduced.

`dotnet test src/zenith.Tests/zenith.Tests.csproj` — **702/702 passing**, unchanged from before this
extraction (same count as after Phase XIV added Cow — this step added no new tests because it
changed no observable behavior, only where the logic lives). This includes, specifically:

- `ZombieSystemTests` — bootstrap spawn, retargeting, chase/obstacle pathing, attack-validates-and-
  removes-exactly-once, out-of-reach no-op, lethal-damage-refused-when-loot-full.
- `SkeletonSystemTests` — ranged behavior, loot, XP.
- `CowSystemTests` — bootstrap spawn, wander-without-player, never-attacks, melee kill → loot + XP,
  out-of-reach no-op, lethal-damage-refused-when-loot-full, late-join replication, projectile kill →
  XP.
- `ProjectileSystemTests` — projectile-vs-zombie still routes through `ZombieSystem.TryApplyDamage`
  correctly (projectile kills were never touched by AI/movement, and the test suite confirms the
  refactor didn't disturb that call path).
- `ExperienceTests` — XP credit for both melee and projectile kills, across mob types, still correct
  through `MobKillReward` (untouched, called identically from inside the new shared helper).
- `DeathLootAtomicityTests` and the floor-drop capacity tests — the "refuse the whole lethal
  transition if loot can't be stored" invariant, now enforced by the one shared implementation
  instead of three separate copies of it.

No behavior changed; the test suite is the proof, not an assertion.

---

## Step 5 — Review

**1. Did the abstraction reduce real duplication?**
Yes, measurably: 133 lines of provably-identical code (confirmed byte-for-byte in Step 1, not just
"similar-looking") collapsed into one implementation used three times. Nothing was extracted on
spec — every line moved into `GroundMobCombat` was already duplicated three times before this step.

**2. Did it keep AI ownership concrete?**
Yes. `IGroundMob` has zero behavior members. Targeting (`Zombie`), ranged fire control (`Skeleton`),
and wandering (`Cow`) remain private methods on their own systems, calling nothing from
`GroundMobCombat` and implementing nothing from `IGroundMob` beyond data access. The movement
collision probe and move-replication batching — which *are* behavior, not bookkeeping — were
deliberately left duplicated at 2 instances rather than forced into the shared helper to keep the
"three real consumers" bar honest.

**3. Does adding a fourth ground mob become easier?**
Yes, concretely: a fourth melee-capable ground mob needs its own entity type (`: IGroundMob`,
seven members it likely already has), its own AI, and then exactly two lines calling
`GroundMobCombat.ApplyPlayerMeleeAttacks`/`TryApplyDamage` with its own store/loot/XP — not another
copy of the ~45-line death/loot/XP/removal block. If a fourth mob also needs movement, `TryMove`
would then have 3 instances, which is the point at which this same document's own reasoning says to
extract it too — not before.

**4. Did we avoid creating a premature entity framework?**
Yes. No base class, no component system, no generic `Actor` concept, no spawn/AI abstraction.
`GroundMobCombat` cannot spawn a mob, decide what it targets, or move it — it can only apply damage
that's already been decided to a mob whose position/health it reads through seven interface members.

**Result, in the words the brief asked for:**

> Ground mob combat bookkeeping became shared while mob behavior remains concrete.

Not "all entities became one abstraction" — `IGroundMob` has no method a caller could use to treat a
Zombie and a Cow interchangeably in AI, movement, or spawning; only in "did this attack land, and
what happens if it kills."

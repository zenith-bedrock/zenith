# Phase XIV — Gameplay Expansion & Domain Pressure

## Choice of feature

Of the four candidate areas (advanced combat, more entities, crafting expansion, world
interaction), **Option B — more entities** was chosen deliberately, not arbitrarily: it is the one
area where Phase XII and Phase XIII had each already written down an explicit, named trigger
condition —

> "Two is a coincidence; three is a pattern. [...] The trigger for revisiting this is explicit: a
> third 'living, AI-driven, meleeable-or-shootable' actor type." — Phase XII, §5

Adding a third ground mob is the most direct way to either produce that evidence or fail to produce
it. Rather than speculate about whether Zombie/Skeleton duplication "would" become a problem with a
third mob, Phase XIV adds one and measures.

A **passive** mob (Cow) was chosen over a third hostile mob specifically because it tests a
different axis than "another chase-and-melee zombie" would: does the Store+System+replication shape
generalize to an actor with genuinely different AI (wander, never targets or attacks a player), or
does it turn out the shape was implicitly zombie-shaped all along? It also happens to be real,
useful gameplay progress (a farmable food/leather source), not a synthetic stress test.

---

## What was implemented

```text
Player melee attack (existing AttackIntent, reused unchanged)
        |
        v
CowSystem.ApplyPlayerAttacks -> TryApplyDamage
        |                              |
        | wander AI (new)              +--> FloorDropFanout.TryDeposit (existing, reused unchanged)
        v                              +--> MobKillReward.AwardExperience (existing, reused unchanged)
CowSystem.Wander -> TryMove                +--> Protocol.SendRemoveActor / SendHealth (existing)
        |
        v
ActorInterest-gated ReconcileViewers -> SendAddCow / SendMoveActorAbsoluteRaws
```

- `Cow`/`CowStore` (`Gameplay/Cow.cs`) — same 6-field shape as `Zombie`/`Skeleton`, plus two
  wander-specific fields (`WanderDirectionX/Z`, `WanderChangeAtTick`).
- `CowSystem` (`Gameplay/Systems/CowSystem.cs`) — registered in `ZenithServer.RegisterWorldSystems`,
  fixed diagnostics timing `tick.system.cow`.
- `EntityProtocol.SendAddCow` — identical shape to `SendAddSkeleton` (no special metadata needed;
  Skeleton already proved a mob doesn't need `ZombieMetadata=true` to render correctly).
- Loot: 1× `minecraft:beef` per kill. XP: 1 point (vanilla passive-mob parity — lower than a hostile
  mob's 5).
- **Reused completely unchanged, zero modification:** `FloorDropFanout` (loot deposit/capacity
  check), `MobKillReward.AwardExperience` (XP credit — works identically for a passive mob because
  it dispatches on `DamageSource.OwnerRuntimeId`, not on mob type), `ActorInterest.Includes`
  (visibility gating), `DamageSource.MeleeFrom`/`DamageSource.Projectile` (attribution), the
  `AttackIntent` mailbox, `HealthState`. This is itself a finding: the *player-facing* half of
  combat (attack intent → damage source → kill reward → replication) already generalized across mob
  types with zero changes needed. The duplication that follows is entirely on the *mob-side* half.

8 new tests (`CowSystemTests.cs`): bootstrap spawn, wander-without-a-nearby-player (proves it's not
target-seeking), never-attacks-a-player-even-when-adjacent, melee kill → loot + XP, attack-outside-
reach no-ops, lethal-damage-refused-when-loot-capacity-full (mirrors Zombie's atomicity test),
late-joining-player replication, projectile kill also awards XP (proving `MobKillReward` generalizes
across the attack-method axis too, not just the mob-type axis). Full suite: 702/702 passing
(694 + 8 new).

---

## The evidence Phase XII/XIII asked for

With Cow as the third instance, here is the exact, measured duplication — not an impression, a
line-by-line comparison of `ZombieSystem.cs`, `SkeletonSystem.cs`, `CowSystem.cs`:

| Method | Zombie | Skeleton | Cow | Verdict |
|---|---|---|---|---|
| `ApplyPlayerAttacks` (reach check + consume attack intent + call TryApplyDamage) | ✅ | ✅ | ✅ | **Byte-identical structure**, 3 instances. Only the damage constant differs. |
| `TryApplyDamage` (guard → apply → health broadcast → death → loot deposit → XP → removal broadcast) | ✅ | ✅ | ✅ | **Byte-identical structure**, 3 instances. Only loot item name, XP amount, and the exception message string differ. |
| `CanDropLoot` | ✅ | ✅ | ✅ | **Byte-identical**, 3 instances (one line each). |
| Reconcile add/remove via `ActorInterest` + health broadcast on spawn | ✅ | ✅ (no move-tracking) | ✅ | **3 instances** of the core add/remove/health shape; Zombie and Cow additionally track `_lastProjected` for move diffing, Skeleton does not (it never moves). |
| Batched move replication (`ReplicateMoves` + `ProjectedPosition.MeaningfullyChanged`) | ✅ | ❌ (stationary) | ✅ | **2 instances.** Skeleton has no equivalent — it never needs one. |
| `TryMove` bounded local collision probe | ✅ | ❌ (no movement at all) | ✅ | **2 instances.** Byte-identical logic (two empty body cells + one supporting cell). |
| Target/AI acquisition (`FindOrAcquireTarget`/`FindTarget`/`Wander`) | chase, nearest-player | ranged, nearest-player + range check | wander, no player awareness | **Genuinely different in every one of the three** — this is where the mobs actually differ and where no duplication exists or should be removed. |

**This crosses the brief's own stated threshold** ("three different systems now need the same
concept") for four methods (`ApplyPlayerAttacks`, `TryApplyDamage`, `CanDropLoot`, and the
add/remove/health half of viewer reconciliation), and sits at a 2-instance "watch, not yet" state
for the movement-specific half (`TryMove`, `ReplicateMoves`).

---

## Decision: document the evidence; do not extract in this pass

Per the brief's own required process for crossing a "create a boundary" threshold — *"1. document
the evidence; 2. explain why current code becomes limiting; 3. propose the smallest evolution;
4. implement only after validating the need"* — steps 1–3 are done above and below. Step 4
deliberately was **not** taken in this same pass, for a reason specific to this codebase's history,
not general caution:

- Phase XII already evaluated and explicitly rejected a `LivingActor` base type when only two mobs
  existed, reasoning that "no third mob type exists to triangulate a real contract from." That
  reasoning has now been answered by evidence rather than argued around — but the answer should be
  acted on deliberately, with its own focused change and its own test run, not folded into the same
  commit that adds the gameplay feature that produced the evidence. Coupling "new mob" with
  "refactor three combat systems" risks a regression in either being harder to isolate if something
  breaks.
- The extraction is real but genuinely small — a shared static helper (not a base class, not an
  interface, not a component), most plausibly shaped as:

  ```csharp
  static class GroundMobCombat
  {
      public static bool TryApplyDamage<TMob>(
          TMob mob, DamageSource source, float amount, IReadOnlyList<Player> online,
          HashSet<(long, long)> replicated, World world, PlayerManager players,
          StackId lootItem, int killExperience,
          Action<TMob> remove, Func<TMob, bool> isActive, ...) where TMob : ...
  }
  ```

  Every concrete sketch of this ends up needing 6–8 parameters or a small capture-heavy delegate set
  to stay type-agnostic without a shared interface — which is real evidence *against* a quick
  extraction being obviously smaller/clearer than the 3× duplication it would replace. The
  alternative (a small shared `IGroundMob { long EntityId; float PositionX/Y/Z; HealthState Health;
  bool IsActive; void Remove(); }` interface implemented by all three, with the shared logic as
  static methods over that interface) is more promising — it avoids inheritance entirely, keeps each
  System owning its own store/AI, and only unifies the part that is provably identical. **This is
  the proposed smallest evolution**, not yet implemented.

**Recommendation, pending approval:** extract `IGroundMob` (interface only, no behavior) plus a
`GroundMobCombat` static helper class covering `TryApplyDamage`/`CanDropLoot`/`ApplyPlayerAttacks`,
in a follow-up, focused change — touching only `Zombie.cs`/`Skeleton.cs`/`Cow.cs` (implement the
interface) and the three `*System.cs` files (call the shared helper instead of the duplicated
private methods), with the full combat/kill/loot/XP test suite (`ZombieSystemTests`,
`SkeletonSystemTests`, `CowSystemTests`, `ExperienceTests`) as the regression gate. `TryMove`/move
replication stay concrete at 2 instances — one more mob with movement would cross that threshold
too, but Cow's wander alone does not yet prove it beyond Zombie's existing one case in a way that
outweighs the cost of generalizing movement (which is more behaviorally sensitive — chase vs. wander
vs. stationary already differ per mob; a shared movement primitive risks constraining a fourth mob's
AI in a way a shared damage/loot/XP primitive does not).

---

## Rejected alternatives

- **A third hostile mob instead of a passive one** — would have added evidence for the exact same
  duplication Zombie/Skeleton already showed, without testing whether the Store+System shape
  generalizes past "aggressive melee/ranged AI." Cow is strictly more informative for the same
  implementation cost.
- **Extracting the shared combat logic in the same change as adding Cow** — rejected per above:
  couples a gameplay feature's correctness to a simultaneous cross-cutting refactor of two already-
  shipped, already-tested combat systems. Better as its own small, focused, separately-reviewable
  change.
- **A generic `Mob`/`LivingActor` base class** — still rejected, for the same reason Phase XII gave
  and this phase's own extraction sketch reconfirms: the parts that are genuinely identical
  (damage/loot/XP/removal) do not need inheritance to share, and the parts that differ (AI,
  movement) would be forced into virtual-method overrides that make each mob harder to read in
  isolation, not easier — exactly the failure mode "Cada comportamento continua concreto" was
  written to avoid.
- **A generic actor/entity framework, ECS, or component system** — no evidence for this appeared.
  Adding Cow required zero changes to `ActorInterest`, the wire protocol shape (`AddActorPacket`
  already generalizes via `EntityType` string), or the diagnostics/composition-root pattern (one
  more `RegisterWorldSystems` line, one more fixed timing entry) — all four already-proven
  extension points absorbed a third mob type without modification. That is evidence the current
  architecture *isn't* limiting for adding entities, only for the small slice of combat logic named
  above.
- **Ranged (projectile) hit-testing for Cow** — discovered, not implemented. `ProjectileSystem`'s
  hit-testing (`FindHitZombie`) is Zombie-specific; Skeleton was already excluded from being
  projectile-hittable before this phase (a pre-existing gap, not one introduced here). Cow follows
  the same precedent (melee-only) rather than introducing a third inconsistent case. If a real
  feature ever needs "any mob is arrow-hittable," that is the trigger to generalize
  `FindHitZombie` into a mob-agnostic hit test — not done now since nothing demands it.

---

## Validation

- Unit tests: 8 new (`CowSystemTests.cs`), full suite 702/702.
- Multiplayer/replication: `A_late_joining_player_is_replicated_the_already_active_cow` exercises
  two simultaneous players and asserts the second receives the already-active cow via the same
  `ActorInterest`-gated path Zombie/Skeleton already use.
- Persistence: **none added, correctly** — mobs are not persisted in this codebase (Zombie/Skeleton
  aren't either; they're RAM-only runtime actors, matching the project's established "measure
  before persisting" stance). Reconnect is a non-issue for the same reason.
- Diagnostics: added exactly one fixed timing (`tick.system.cow`), the same pattern used for every
  other system — no new metric type, no per-mob dynamic metric (matches Phase XI/XIII's "no metrics
  without a pending decision" rule).

---

## What new pattern appeared after adding real gameplay?

**Outcome 2 of 3: a small primitive is justified — but not yet implemented.** The evidence is now
concrete and quantified (four methods duplicated verbatim across three systems); the shape of the
smallest reasonable fix has been sketched (`IGroundMob` interface + `GroundMobCombat` static helper,
explicitly not a base class); and the reason to defer implementation to a follow-up change is
itself principled (decouple new-feature risk from refactor risk), not avoidance.

What did **not** happen is equally informative: no pressure appeared on `ActorInterest`, the wire
protocol, the composition root, or diagnostics — all four absorbed a third, behaviorally distinct
actor type with zero or one-line changes. The architecture is not generally under-abstracted for
"add an entity"; it is specifically under-abstracted for one narrow slice of combat bookkeeping,
and only exactly that slice is proposed for evolution.

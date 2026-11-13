# Handoff — runtime, entities, composition (2026-11-13)

Read first: `AGENTS.md`, `docs/architecture.md` (GameLoop, Composition), `docs/entities.md`, ADR §149 in `docs/decisions.md`.
Supersedes the git-state and open-work parts of `handoff-2026-11-12-data-from-bds-packs.md`; the BDS-pack learnings in that file stay valid.
User writes Portuguese; reply in Portuguese. Use they/them for unknown pronouns.

## Standing rules from the user
- **Zenith is not assumed correct.** A passing test or smoke is not proof; always say what was verified and what was not.
- Priorities: separation of responsibilities, decoupling, DX, performance; deep refactors welcome. **No legacy/gambiarra**, data coherent, nothing silently dropped. Code "bem adequado, similar ao bedrock server". **Finish a chosen item completely: "não quero nada pendente/parcialmente feito".**
- No showcase mobs kept alongside the real thing; vanilla behaviour is refined later.
- Work directly on `develop` (zenith) / `main` (smoke bot). Commit trailers: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` + the session's `Claude-Session:` line. **Push only with explicit approval** (the user approved pushing up to `938322e`; later commits are local).
- Smoke bot (`G:\zenith-workspace\zenith-smoke-bot`) has the user's uncommitted WIP in `src/zombie.ts` and `src/floorPickup.ts`: never commit or touch them; temp scripts there (`src/_*_tmp.ts`) must be deleted, `git status` there must show only those two files.
- `essential-reference` is untrusted, never run. `cinnabar-reference` is PolyForm Noncommercial: behaviour reference only, never copy code or data. Tooling must be C#.
- Tooling quirks: no `pkill` (use `taskkill //IM zenith.exe //F`); building while `zenith.exe` runs fails; Bash heredocs mixing quotes and code break (use the Write tool for files); run builds/tests from the repo root (`dotnet build zenith.sln -c Release`, `dotnet test src/zenith.Tests -c Release --no-build`, ~2 min); never `git stash` with untracked work you need; to compare against an older commit use a `git worktree` in the scratchpad and remove it afterwards.

## Git state
- `origin/develop` = `5a024fb`; the `Player` split is committed on top of it (ask before pushing).
- Smoke bot untouched.

## What changed in this stretch (all verified by tests: 1341/1341 in `zenith.Tests`)
- **Gameplay vs transport:** `Gameplay/Ports/ClientPorts.cs` (`IClientChannel`: Entity/Inventory/World/Chat/Ui); Gameplay may not reference `Zenith.Session`/`Zenith.Protocol` (IL-level architecture test). `Player` takes `Level`/`ItemPalette` in its constructor and a client via `AttachClient`.
- **Composition:** `Server/Composition/*` (`ServerServices`, `IGameplayModule`: PlayerInput, Entity, World modules); `GameplayCompositionTests` pins the 32-system order.
- **Spawning:** `/summon <mob>` (`MobSpawner`), natural spawning from `data/spawns.json` (`NaturalSpawnSystem`, `doMobSpawning`). The night path is only unit-tested; live runs saw cows.
- **Tick pacing:** dedicated `zenith-game-loop` thread at `AboveNormal` (`GameLoop.LoopThreadPriority`), absolute deadlines (`TickSchedule`), `PreciseWaiter` (Windows high-resolution timer + spin). `--tick-jitter` benchmark. At loop priority: p99 <= 0.6 ms, max 2-4 ms over 20 s; at normal priority the same code showed 2-6 ms OS-preemption stalls. Metrics `tick.wake-lateness`, `tick.skipped`.
- **Interest cost:** `ViewerInterestTracker` + `PlayerChunkTracker.Version`; `ActorReplication.BeginTick` must be the first call of a species' tick; an unmoved actor is re-checked only against players whose interest changed (contract: a change made mid-tick lands one tick later). Mixed roster 100 players / 1000 actors: mean tick ~21-24 ms to ~13-16 ms.
- **One actor path (ADR §149):** every species, projectile included, is an ECS actor on `MobCore` + `ActorReplication` (`ActorAnnouncer`, `Retire`, `ReconcileAll`) + `DamageDispatch`. `Bat`, `Villager`, `*Store`, `IDamageableActor`, `GroundMobCombat` are gone. To add a species: a state struct, a row in `data/mobs.json` (name must parse as `MobKind`), a system on `MobCore`/`ActorReplication`, a dispatch registration, a spawner entry.
- **Toolchain:** SDK 10.0.303, `LangVersion` 14 pinned in `Directory.Build.props`.

## Known caveats (measured, not fixed)
- `smoke:projectile`, `smoke:skeleton`, `smoke:mixed-combat` time out waiting for the first `add_entity`. Confirmed for `smoke:projectile` on the commit before ADR §149 as well; cause not investigated (the bot may expect the removed showcase mobs or an older projectile intent). Use `/summon` + a temp bot script to check live replication; that showed bat, villagers and skeleton arrows announced, moved and removed.
- `smoke:skin` fails inside the bot (`SizeOf error for undefined` at `write("player_skin")`) before any network traffic; not investigated.
- One run of the full suite failed `WorldGenerationDiagnosticsTests.World_reuses_bounded_base_column_cache_after_generation_completes`; it passes alone and in 3 later full runs. Treat as a load-sensitive flake until proven otherwise. `TickPacingTests` timing assertions are best-of-5 for the same reason.
- `--runtime-load` egress totals depend on wall-clock time (peers never ack, so RakNet resends scale with elapsed time); compare them only between runs with the same pacing.
- Tick tail outside the dev machine, Linux/macOS pacing path and pre-1803 Windows fallback are untested. No retail-client validation of anything above.
- Fish and Bat now use batched move replication; Fish was not re-run live.
- Remaining O(players) scans in the actor path: `DespawnLifecycle.EvaluateDespawn`, melee attack scan, `ReplicateMoves` (per peer over all actors), `Retire`, `Prune`. Roughly 1-2 ms/tick at 100 players / 1000 actors; a per-actor viewer structure would remove them.

## Open work (the user's weakness list, minus what is done)
1. ~~`Player` split~~ done: `Intents`/`Dig`/`Windows`/`Effects`/`Replicated` components (`docs/architecture.md` "Player").
2. 252 "ADR §" references / 148+ ADRs and a ~2-minute test suite (item 6): consolidate, make the suite faster.
3. Item 7: nothing validated with a retail client; night spawns never run live; `Consume`/`CraftResultsDeprecated` are no-ops; only chest/personal/crafting-table windows; `count==0` means whole stack; item/creative/biome/loot/trading catalogs not yet from the pack source (see the 2026-11-12 handoff, "Open work").
4. Per-system tick-cost benchmark exists (`--runtime-load --mixed-roster`); extend it to bat/villager/fish/creeper/enderman/golem (it covers zombie, minecart, cow, spider, skeleton, projectile).

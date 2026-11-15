# Post-impl audit

**Scope:** Current gameplay fixes on develop, within the existing uncommitted protocol audit work. Ground loot, crafting-table admission and player melee reach (ADR 154).
**Verdict:** Pass, subject to the recorded automated checks and real-client follow-up below.

## Blockers

- None found in this change.

## Nits (non-blocking)

- Cinnabar visual/interaction confirmation remains pending; automated handler/tick tests do not certify the client UI.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| Gameplay/WorldInteraction/BlockEditSystem | Preflight and commit ground loot on tick | Yes |
| Gameplay/Inventory/InventorySystem | Admit table slots only while at a table | Yes |
| Session/Handler/InGameInventoryHandler | Bind requests to matching open/pending window generation | Yes |
| Player/PlayerWindows, PlayerIntents | Pending window and attack mailbox contracts | Yes |
| Gameplay/Entities, World/EntityHitboxes | Player reach and authoritative mob dimensions | Yes |
| Geometry/Aabb | Pure nearest-surface distance | Yes |
| Protocol/EntityProtocol | Project domain dimensions | Yes |

## Freeze / anti-pattern scan

- [x] No new frozen abstraction without ADR
- [x] No Network/ revival
- [x] Handler/thread rules OK: inventory, world and health mutations stay on the gameplay tick
- [x] Intent queue policy preserved

## DX / tests

- Leaf tests: zenith.Tests; new table handler-to-tick and melee tests, updated mining/loot tests.
- Ground loot is visible before pickup, including when inventory has space. Full floor capacity refuses the break before destroying the source. Tool eligibility, creative and gamerule behavior remain enforced.
- Table test opens the window, transfers eight planks into table slots, crafts a chest and takes the result. Reopened-window generations reject stale requests.
- Melee uses eye-to-hitbox reach independently of mob AI range; an out-of-range targeted click is consumed rather than landing later. Existing health invulnerability remains unchanged.
- Final Release run: all 1,482 zenith.Tests passed. The solution run also passed all 305 tests in the other projects; its three mining failures were outdated bag-first expectations, updated and included in the successful final main-project run. Total: 1,787 passing tests across the final runs. Diff whitespace check passed.
- Server restarted as PID 37168, with RakNet UDP 19132 and NetherNet signaling TCP 19133 listening, LevelDB world preserved, startup ready and stderr empty.

## Should not be committed

- Builds/ binaries, runtime logs, local configuration and persistent test world remain ignored.
- Earlier unrelated dirty files are retained; this report does not re-certify every preceding audit change.

## Suggested follow-ups

1. Reconnect Cinnabar and confirm table slot interaction, visible block drops and immediate valid hits using both configured transports.

## Reference comparison requested by the user

- PocketMine stable World::useBreakOn publishes ordinary block drops through dropItem; dropItem defaults to a 10-tick pickup delay. Zenith now agrees on ground-first loot and delay, but its cell-based floor store does not reproduce PocketMine's randomized item motion.
- PocketMine stable Player::attackEntity checks canInteract with MAX_REACH_DISTANCE_ENTITY_INTERACTION (8). Zenith's 3-block eye-to-AABB reach is a distinct current policy, not a copied PocketMine limit or a measured BDS parity claim.
- The table admission fix restores Zenith's existing grid/session design. Shaped recipe matching and grid return on close remain existing limitations (ADR 139), not solved by accepting table transfers.
- BDS parity for the three reported symptoms still requires controlled client comparisons; the previously successful BDS chest placement test does not establish crafting/combat parity.
- References inspected: https://github.com/pmmp/PocketMine-MP/blob/stable/src/world/World.php and https://github.com/pmmp/PocketMine-MP/blob/stable/src/player/Player.php (2026-11-15).

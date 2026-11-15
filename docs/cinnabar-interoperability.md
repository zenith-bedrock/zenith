# Cinnabar interoperability — protocol 2225

## Scope and evidence (2026-11-13)

Installed Cinnabar core 0.2.1 (`8126f98c94d3306f8c730a55b89d5cf2d513b8dd`),
Zenith's stable 2225 target, and isolated official BDS 1.26.63.1 were used.
The user confirmed breaking blocks, collecting drops, placing blocks with correct inventory
counts, and placing/opening a chest in the official BDS through Cinnabar.
This comparison corrected an earlier, unsupported attribution of the transaction failure to the client.

The sibling Cinnabar checkout is a behavioral reference under PolyForm Noncommercial.
No reference code, fixtures, compiled registries or game assets are imported into Zenith.
The regression body in `InventoryTransactionDtoTests` comes from our own live client capture.

## Transaction alignment

`InventoryTransaction` starts with a signed legacy request ID, **one optional-array presence byte**,
the optional legacy slot array, then the transaction variant and inventory action count.
Request ID zero does not remove the presence byte. The previous migration incorrectly inferred
array presence from the request ID and treated an absent-array marker as the Normal variant.
ItemUse bytes were then interpreted as inventory actions, causing rejection/resynchronization.
Client prediction could briefly decrement a chest stack before the authoritative count returned.

Read the same optional-array gate inside AuthInput's packed legacy item-use transaction.
Do not restore obsolete extra markers around required transaction/action fields. Tests cover
the untouched captured ItemUse, standalone actor/release/normal transactions, embedded item use,
and placing with/without a predicted legacy slot change while consuming exactly one item.
The historical interpretation in `protocol-churn.md` is superseded for this gate.

## Terrain and join phases

The installed core initially received 40 of 81 radius-4 columns: the remaining 41 were sent
on RakNet ordering channel 1, whose indexes collided in the client's single receive queue.
All reliable Bedrock batches now use channel 0 (ADR §151). Encoding workers, bounded queues,
publication budgets and the retransmission correction from ADR §94 remain.
Radius requests are handled during pre-spawn, spawn response and play; the tick owns radius
application. Packet 321 (client camera aim assist) is recognized as unsupported control input.

## Published-binary regression (2026-11-15)

The executable in `Builds/publish/zenith/release/` still contained the old channel
split and missing SpawnResponse radius handler. A fresh reproduction through the
installed core received 40/81 columns and logged Unhandled Data Packet 69; the
current source/publication delivered all 81. Source changes alone had not updated
that executable. A corrected publication was prepared under
`Builds/publish/zenith/chunk-fix/`, without replacing or restarting the old process.
Startup now logs the assembly MVID and running directory to identify the binary.

The reusable `tools/cinnabar-terrain-probe` exercises exact chunk-coordinate sets
through the installed core on RakNet: initial join, six movement waypoints with
negative coordinates and re-entry, radius shrink and expansion. All nine stages
passed on the new publication; the old publication fails at join with 41 missing
columns. This establishes delivery through the core, not terrain decoding/meshes
in the renderer. See its README for the client MCP and personal terrain-mod options
for the next rendered E2E layer. The offline HTTP NetherNet attempt was rejected
by this installed core before server connection because that target requires a
Microsoft session; no account cache was supplied or modified.

## Persistent-world restart and containers (2026-11-15)

An authorized restart exposed a second cold-world fault absent from the disposable
empty-world test: parallel hydration called ChestStore.TryLoadFromBlob from workers,
writing ordinary dictionaries concurrently. It aborted pre-spawn with a collection
corruption error. ADR 162 moves chest seeds onto the tick before column publication,
keeps pending bytes in the existing chunk hydration map and preserves live chests.
The failed disconnect also exposed RakNet Disconnect using the ConnectedPong ID;
it now emits the proper 0x15 instead of a truncated 0x03 pong.

After applying these corrections, the persistent world passed the nine-stage
terrain route through the installed core. The `--containers` scenario then crafted
and placed a table, opened/filled its 3×3 grid, crafted and placed a chest, opened
27 slots, deposited/withdrew items, and closed/reopened both containers without
restoring consumed items. Stack counts and positive protocol stack IDs were checked
through server replies. This is a core gameplay round trip, not a rendered UI test.

The active publication is `Builds/publish/zenith/chunk-fix-hydration/`; the earlier
`chunk-fix/` publication was also refreshed to the same corrected binaries. Both
use the existing persistent world path, while disposable probes used separate data.

## NetherNet transport evidence

Attach message handlers when the native data channel is announced. Deferring attachment to
the next server poll lost fast clients' initial RequestNetworkSettings. The integration test
pauses polling while the peer opens/sends and verifies that the first frame survives; restoring
the old code makes that test time out.

FULL login still requires the exact issuer-authenticated signaling token. Offline self-signed
signaling and game login may use independent tokens/keys; accepting self-signed login remains
governed by `auth.accept`. An authenticated signaling identity cannot downgrade to self-signed.

A disposable C# bridge probe through the installed core received 81 distinct columns, placed a
chest and received its 27-slot inventory over RakNet (about 3.2 s) and NetherNet (about 6.7 s).
Those measurements precede the final presence-gate correction and used a hand-built transaction;
they establish transport/terrain/container delivery, not rendered-client gameplay acceptance.
The user subsequently confirmed placing/opening chests and correct inventory behavior against
the corrected Zenith in the rendered Cinnabar client. This does not establish rendered acceptance
of skins, crafting, effects or every NetherNet gameplay path.

## Deeper packet review (2026-11-14)

The skin codec now matches two own BDS PlayerList captures byte for byte, including nonempty
animations/persona/tints. The capture's 256px skin also exposed the former 128px size cap;
the codec now uses the protocol's 4096px bound. The fallback shares the full serializer.
MobEffect uses an unsigned runtime ID. Player effects and motion carry the latest input tick
processed on the gameplay owner. AddPlayer/AddItemActor retain durability NBT and shield extras.
Text/AuthInput counts, ISR descriptor variants and complete-payload checks have regression coverage.
The implementation and remaining capture-dependent questions are tracked in the audit report.

## Resources and local operation

SDK output is under ignored `Builds/`; the runnable Release server is
`Builds/bin/zenith/release/zenith.exe`. The local test config stores the world under
`Builds/data/worlds/world`, enables both transports, and exposes RakNet on 19132 and NetherNet
HTTP signaling on 19133. These operator-specific files are ignored, not repository defaults.

Cinnabar's downloaded resource pack contains client textures, geometry and presentation metadata.
It does not provide a complete authoritative behavior/loot/recipe implementation for Zenith.
Use official BDS data and Zenith's existing import/manifest workflow for server-owned resources;
do not depend on the client installation or copy Cinnabar's compiled registries into production.

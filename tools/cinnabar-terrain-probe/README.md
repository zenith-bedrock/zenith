# Cinnabar terrain delivery regression

This C# diagnostic launches the installed `bedrock-core.exe` as an **offline**,
isolated bridge client. It does not launch Zenith, read account caches, place
blocks, or modify the Cinnabar checkout/install. Use a disposable flat Zenith with
spawn at the origin, Creative mode, accepted offline/self-signed identity and a
maximum chunk radius of four. The route submits synthetic AuthInput positions;
this is a streaming test, not movement simulation or anticheat validation.

```powershell
dotnet run --project tools/cinnabar-terrain-probe -c Release -- `
  'C:\Program Files\Cinnabar\bedrock-core.exe' '127.0.0.1:19542' `
  'Builds/terrain-probe-result'
```

The probe checks exact coordinate coverage, not just packet/column counts:

- Join: all 81 radius-four columns, including those streamed after spawn release.
- RequestChunkRadius (decimal 69) again during SpawnResponse, before initialization.
- Move through negative coordinates and the reported route to chunk `(2,-1)`.
- Return to `(0,0)` after forgetting columns outside each publisher square.
- Shrink to radius two (25 columns), then expand to four (81) without movement.

Each stage has a 15-second deadline, the whole run 90 seconds. A failure exits 1
and writes missing coordinates; success exits 0 and writes `result.json`. Artifacts
and the core's redacted log belong under ignored `Builds/`. Only the core child
started by the probe is stopped. Existing Cinnabar processes are untouched.

Protocol/IPC assumptions are explicit in Program.cs and target Zenith's 2225 and
the installed Cinnabar 0.2.1 core. No Cinnabar source, fixtures or assets are copied.
The installed core requires a Microsoft session for its HTTP NetherNet target;
this offline probe currently establishes RakNet delivery only.

## Table and chest round trips

Append `--containers` to run a survival inventory scenario instead of the terrain
route. Use a fresh probe identity with the normal starter hotbar (oak planks in
slot 2, dirt in slot 1, chest identity in slot 5). The probe embeds the same recipe
asset as Zenith to resolve recipe network IDs; use matching server/probe builds.

It crafts a table in the personal grid, places it and checks the stack decrement,
opens its 3×3 grid, moves eight planks into the ring and crafts a chest. It closes
and reopens the table and verifies the consumed ingredients did not reappear.
Then it places the chest, opens 27 slots, deposits and withdraws three dirt,
closes/reopens and checks fresh authoritative contents. Positive server stack IDs
from InventoryContent/ItemStackResponse are retained across these requests.

This mode **does place test blocks** in the server's world, around `(82, -60, 64)`
on the default flat terrain. It chooses two clear cells and refuses to replace
existing blocks. Use a disposable world for repeated automation. It reports test
coordinates in result.json. Chest orientation changes the block-state runtime ID;
successful typed container open validates its material after placement.

`core-gameplay-roundtrip` proves server responses through the installed core. It
does not simulate the Rust client's prediction, mouse clicks or visible screens.

## Q-drop flight

Append `--drop` to exercise the correlated Normal InventoryTransaction used for
hotkey dropping, with a fresh probe identity and the normal starter hotbar. The
probe moves to `(128.5, flat spawn Y, 128.5)`, drops one stone, checks the elevated
AddItemActor origin and forward velocity, then observes absolute movement and
landing. After the pickup delay it approaches the landing location and requires
TakeItemActor, RemoveActor and the original inventory count. The test does not
edit blocks and collects the item it threw. It records actual travel and movement
packet count in result.json. This still establishes core/server round trips,
not visible frames from the Rust renderer.

## What this proves

`success` means LevelChunk packets arrived through the real Cinnabar core at all
route stages. It does **not** prove terrain decode, mesh publication or rendering.
LevelChunk uses inline subchunks; Zenith does not advertise request-mode subchunks
or blob caching in these tests. RequestChunkRadius negotiates view distance; it
does not ask for an individual missing column.

For rendered E2E, Cinnabar's development checkout already provides client MCP:
`docs/agents/client-mcp.md`, `tools/cinnabar-mcp`. Its developer-control client can
drive input and report `state`/`wait_for` chunks_loaded. Shipping packages omit
developer control, so an installed release is insufficient for that endpoint.
Check readiness evidence by coordinate/cohort as well as screenshots; resident
counts alone can include old or unrelated columns.

A personal mod is also feasible: the modding terrain API exposes `read-region`
with a loaded bitset, queried chunk revisions and explicit pending/unavailable
states. It needs the terrain permission and a version-matched SDK. This can check
terrain admission, but still does not establish that meshes were drawn. Prefer
the existing developer-control/acceptance surface for end-to-end rendering tests.

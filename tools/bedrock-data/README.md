# Stable Bedrock asset import

Developer tooling only. All tooling is the C# `bedrock-data` CLI (`dotnet run --project tools/bedrock-data -- <command>`, tests in `tools/bedrock-data.Tests`). The one exception is the Endstone plugin in `endstone/`: Endstone only loads Python plugins, so that script cannot be C#. Runtime loads embedded assets and does not download BDS or run Endstone. The current bundle comes from BDS 1.26.52.3 / protocol 2193, queried through Endstone 0.11.13. Versions and SHA-256 hashes are recorded in `src/zenith/data/manifest.json`.

## Extract a bundle

1. Install Endstone and the matching official BDS in a temporary directory. Verify the archive SHA-256 against Endstone's release metadata. Use a separate offline test world and ports.
2. Install `endstone/endstone_zenith_data.py` as an Endstone plugin, with entry point `zenith_data=endstone_zenith_data:DataExportPlugin`. Supply `block-candidates.json` and `item-candidates.json` in the BDS working directory. The former is a JSON array of typed block entries (`name`, `states`, with each state containing `type` and `value`) from the previous palette plus reference states; the latter is the decoded BDS ItemRegistry (`itemstates`). The plugin queries BDS for each candidate, expands new state domains, discovers new block types from the item registry, and writes `exported-blocks.json`. Resolve every reported error before import. Candidate enumeration is not a guarantee of all hypothetical states; only states returned unchanged by BDS are accepted.
3. Use the separate smoke-bot's matching protocol codec to capture packet **bodies** for ItemRegistry, CreativeContent and BiomeDefinitionList. Decode the captures with that codec. Check exact ItemRegistry and biome roundtrips before importing. The current CreativeContent decoder succeeds, but its encoder differs from the original capture; the creative dump remains a reference for the curated runtime catalog.
4. Normalize item entries into `name`, signed `id`, `component_based`, `version` (legacy=0, data_driven=1, none=2), and optional `component_nbt_b64` containing Network NBT for non-empty definitions. Preserve definitions independently of the component-based flag. Normalize creative groups/items into the existing reference JSON shape, resolving signed block runtime IDs against the validated block export; `block_state_b64` and creative `nbt_b64` use little-endian NBT.

The input directory must contain:

```text
server/exported-blocks.json
capture/item_registry.bin
capture/item_registry-decoded.json
capture/item_palette.json
capture/creative_content.bin
capture/creative_items.json
capture/biome_definitions.bin
```

Capture and normalization belong to the external bot workflow; this CLI does not automate them. Keep raw bundles outside the C# checkout.

## Import and verify

Run from the repository root (replace the bundle path):

```powershell
dotnet run --project tools/bedrock-data -- import C:/Temp/verified-bundle --bedrock-version 1.26.52.3 --protocol 2193 --endstone-version 0.11.13 --bds-sha256 2c9b98d07d2504786996f2335980e88bd969b4a77514925e75471a1349995825
dotnet test zenith.sln -c Release
```

The importer checks the exported version, unresolved errors, duplicate item names and the set of non-empty component definitions. It writes deterministic gzip BigEndian block NBT, normalized item/creative JSON, the exact biome body, and a manifest with source and output hashes. It does not itself perform the capture roundtrip checks or validate the supplied archive file.

Use a fresh `ZENITH_DATA` directory and run `smoke:join` from the sibling bot. Old persisted numeric IDs require a separate version-aware migration before reuse. No operator world is deleted by the importer.

## Recipes

`src/zenith/data/recipes.json.gz` is built from the behaviour packs a stock BDS loads (see below), never from a wire capture:

```powershell
dotnet run --project tools/bedrock-data -- recipes <bds root> <item_registry.json> src/zenith/data/recipes.json.gz --verify <capture dir>
```

It emits the pack's `crafting_table` shaped/shapeless recipes (legacy spellings and `data` values resolved through
the explicit tables in `Recipes/RecipeTables.cs`), the colour-dyeing families BDS builds in code (wool, wool slab/stairs, shulker box,
bundle, cushion, harness) and the few vanilla recipes BDS registers in code (slabs, small wood parts, ...), all keyed
by real item names. Items with a variant (bed/banner colour, ...) carry an aux (ADR §147). Recipes whose variant lives in stack NBT
(firework stars/rockets) or needs a potion model (tipped arrows) are counted and not emitted. `--verify` takes a CraftingData capture as an ORACLE only: it fails if any aux-free
grid recipe a real BDS sends has no equivalent here. Current data: BDS 1.26.63.1 (3819 recipes, 4045 wire grid recipes matched, 0 missing; 44 tipped-arrow and 41 stack-NBT wire
recipes not emitted).

## How BDS loads its catalogs (observed)

`BdsPack.cs` reads them the way BDS does: `behavior_packs/vanilla` + `vanilla_<version>` packs applied in version
order, later files replacing earlier ones of the same name, shipped as `.brarchive` containers (format documented in
the module). Recipes, items, item catalog (creative groups), biomes, entities, loot, trading, spawn rules, structures and
the 98 newest data-driven blocks live there as plain behaviour-pack JSON. Classic blocks and the legacy
`(name, data)` -> flattened item table are compiled into the executable.

BDS also *generates* recipes in code (colour dyeing, slabs, ...): the extractor reproduces those from rules and the
`--verify` oracle proves nothing a stock BDS can craft is missing.

# Stable Bedrock asset import

Developer tooling only. Runtime loads embedded assets and does not download BDS or run Endstone. The current bundle comes from BDS 1.26.52.3 / protocol 2193, queried through Endstone 0.11.13. Versions and SHA-256 hashes are recorded in `src/zenith/data/manifest.json`.

## Extract a bundle

1. Install Endstone and the matching official BDS in a temporary directory. Verify the archive SHA-256 against Endstone's release metadata. Use a separate offline test world and ports.
2. Install `endstone_zenith_data.py` as an Endstone plugin, with entry point `zenith_data=endstone_zenith_data:DataExportPlugin`. Supply `block-candidates.json` and `item-candidates.json` in the BDS working directory. The former is a JSON array of typed block entries (`name`, `states`, with each state containing `type` and `value`) from the previous palette plus reference states; the latter is the decoded BDS ItemRegistry (`itemstates`). The plugin queries BDS for each candidate, expands new state domains, discovers new block types from the item registry, and writes `exported-blocks.json`. Resolve every reported error before import. Candidate enumeration is not a guarantee of all hypothetical states; only states returned unchanged by BDS are accepted.
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

Capture and normalization belong to the external bot workflow; these Python tools do not automate them. Keep raw bundles outside the C# checkout.

## Import and verify

Run from the repository root (replace the bundle path):

```powershell
python tools/bedrock-data/import_data.py C:/Temp/verified-bundle --bedrock-version 1.26.52.3 --protocol 2193 --endstone-version 0.11.13 --bds-sha256 2c9b98d07d2504786996f2335980e88bd969b4a77514925e75471a1349995825
dotnet test zenith.sln -c Release
```

The importer checks the exported version, unresolved errors, duplicate item names and the set of non-empty component definitions. It writes deterministic gzip BigEndian block NBT, normalized item/creative JSON, the exact biome body, and a manifest with source and output hashes. It does not itself perform the capture roundtrip checks or validate the supplied archive file.

Use a fresh `ZENITH_DATA` directory and run `smoke:join` from the sibling bot. Old persisted numeric IDs require a separate version-aware migration before reuse. No operator world is deleted by the importer.

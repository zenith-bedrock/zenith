# Protocol churn checklist

**Last review:** Jul 2026 (CraftingDataPacket §35 — remint after CreativeContent; SetActorData/UpdateAttributes §34)

Cadence: ~trimestral, or whenever the team targets a newer Bedrock client. **Manual** — no remote schema scraper.

## Steps

1. Note the client version under test vs `ServerIdentity.ProtocolVersion` and `VersionName` in [`src/zenith/Server/ServerIdentity.cs`](../src/zenith/Server/ServerIdentity.cs).
2. Spot-check encode layouts against current gophertunnel / Vedrock protocol refs for at least:
   - `ProtocolInfo`
   - `StartGamePacket`
   - `ItemRegistryPacket`
   - `CreativeContentPacket` (Groups + CreativeItems; after ItemRegistry in login)
   - `CraftingDataPacket` (shapeless remint from RecipeRegistry; ClearRecipes; after CreativeContent — §35)
   - `SetActorDataPacket` / `UpdateAttributesPacket` (local HUD seed after BiomeDefinitionList — §34)
   - `LevelChunkPacket`
   - Inventory / ItemStackRequest paths (SAI on)
3. Smoke: login → InGame on that client.
4. If wire broke: fix encode, bump SSOT (ADR §12), add a short decision entry if the bump is non-trivial.
5. Update **Last review** date at the top of this file.

Automation against public schemas remains deferred (ARCHITECTURE freeze: no decorative infra).

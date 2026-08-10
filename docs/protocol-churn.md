# Protocol churn checklist

**Last review:** Aug 2026 — bumped 1001/1.26.33 → 2169/1.26.50 (ADR §79) via `protocol-import pull --source mojang --ref r/26_u4`. **15 packets left pre-Cereal shaped as tracked debt** — see `roadmap.md` § "Cereal migration debt" before assuming any of those 15 match this branch's schema.

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
   - `UpdateAbilitiesPacket` / `UpdateAdventureSettingsPacket` (after §34 attributes; AbilityData SSOT with AddPlayer — §37)
   - `RequestAbilityPacket` (inbound FLYING echo in Creative — §37)
   - `LevelChunkPacket`
   - Inventory / ItemStackRequest paths (SAI on)
3. Smoke: login → InGame on that client.
4. Mismatch clients (protocol ≠ `ServerIdentity.ProtocolVersion`) must see `PlayStatus` 1 (`LOGIN_FAILED_CLIENT`) or 2 (`LOGIN_FAILED_SERVER`) then disconnect — ADR §43. Do not soft-ignore wrong versions.
5. If wire broke: fix encode, bump SSOT (ADR §12), add a short decision entry if the bump is non-trivial.
6. Update **Last review** date at the top of this file.

Automation against public schemas remains deferred (ARCHITECTURE freeze: no decorative infra).

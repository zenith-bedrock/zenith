namespace Zenith.Server;

/// <summary>
/// Identidade de protocolo anunciada no unconnected ping e nos packets de jogo,
/// mais a versão de produto Zenith (release) — não misturar com o wire Bedrock.
/// Não é editável via zenith.yml — deve bater com o que o código realmente encode.
/// </summary>
static class ServerIdentity
{
    /// <summary>Zenith product / release version (changelog, Docker tags, logs). Not on Bedrock wire.</summary>
    public const string ProductVersion = "0.0.2-alpha";

    /// <summary>
    /// Bedrock protocol number (client <c>RequestNetworkSettings</c> / MOTD).
    /// Bumped to 2169 (ADR §79) — <b>known debt:</b> Mojang moved ~23 packets to Cereal
    /// serialization at this protocol. LevelChunk/MovePlayer migrated (ADR §88); PlayerAuthInput
    /// migrated (ADR §89); ItemStackRequest/Response, PlayerSkin, ResourcePacksInfo,
    /// ResourcePackClientResponse migrated (ADR §90 — cross-checked live for the join-path
    /// packets, ItemStackRequest/Response not yet live-validated against a real client);
    /// CraftingData/CreativeContent/AddPlayer/AddItemActor/SetActorData migrated earlier
    /// (ADR §82/§85/§86). Still pre-Cereal (1001-era): <b>StartGamePacket</b> (largest packet in
    /// the server, deliberately deferred — do last or first, not mid-stream) and
    /// <b>PlayerListPacket</b> (per-entry tagged variant; the community schema used to
    /// cross-check every other packet this session is confirmed unreliable for this one — it
    /// omits skin/build-platform fields real clients need, see ADR §90 non-goals — so this one
    /// needs `endstone-bedrock-protocol` as primary source, not rushed). A real 1.26.50 client
    /// will desync/crash on those specific packets until each is migrated — see
    /// <c>docs/roadmap.md</c> "Cereal migration debt" for the tracked list. Deliberate, accepted
    /// debt for private/dev use, not a silent gap.
    /// </summary>
    public const int ProtocolVersion = 2169;

    /// <summary>String de versão de jogo no wire (StartGame / ResourcePackStack). SSOT.</summary>
    public const string VersionName = "1.26.50";
}

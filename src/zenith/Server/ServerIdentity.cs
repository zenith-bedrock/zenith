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
    /// Targets stable Bedrock 1.26.52 (protocol 2193; same protocol schema as 1.26.51).
    /// The 2193 schema adds <c>Hand</c> to ItemUseInventoryTransaction and removes redundant
    /// Cereal accessor-presence bytes from InventoryTransaction, PlayerAuthInput, and
    /// ItemStackResponse. Zenith's implemented packet support is tracked in
    /// <c>docs/protocol-churn.md</c>; this version gate alone does not imply every Bedrock packet
    /// is implemented or verified against a live client.
    /// </summary>
    public const int ProtocolVersion = 2193;

    /// <summary>String de versão de jogo no wire (StartGame / ResourcePackStack). SSOT.</summary>
    public const string VersionName = "1.26.52";
}

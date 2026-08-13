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
    /// The compatibility target is protocol 2168 / 1.26.40: the current live client and the
    /// verified Endstone r26_u4 snapshot both require this exact network version. The existing
    /// 2168+ Cereal packet paths remain in place; this is a wire-identity downgrade, not a blind
    /// source revert. See <c>docs/protocol-import.md</c> for the reconciled-snapshot workflow.
    /// </summary>
    public const int ProtocolVersion = 2168;

    /// <summary>String de versão de jogo no wire (StartGame / ResourcePackStack). SSOT.</summary>
    public const string VersionName = "1.26.40";
}

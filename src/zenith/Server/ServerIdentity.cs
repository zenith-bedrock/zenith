namespace Zenith.Server;

/// <summary>
/// Identidade de protocolo anunciada no unconnected ping e nos packets de jogo,
/// mais a versão de produto Zenith (release) — não misturar com o wire Bedrock.
/// Não é editável via zenith.yml — deve bater com o que o código realmente encode.
/// </summary>
static class ServerIdentity
{
    /// <summary>Zenith product / release version (changelog, Docker tags, logs). Not on Bedrock wire.</summary>
    public const string ProductVersion = "0.0.1-alpha";

    /// <summary>
    /// Bedrock protocol number (client <c>RequestNetworkSettings</c> / MOTD).
    /// Encode de StartGame / LevelChunk segue o layout 1001 (refs Vedrock / gophertunnel).
    /// </summary>
    public const int ProtocolVersion = 1001;

    /// <summary>String de versão de jogo no wire (StartGame / ResourcePackStack). SSOT.</summary>
    public const string VersionName = "1.26.33";

    /// <summary>Git commit SHA curto do build (atualizar no release).</summary>
    public const string GitCommit = "55f536b";
}

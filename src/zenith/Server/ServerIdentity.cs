namespace Zenith.Server;

/// <summary>
/// Identidade de protocolo anunciada no unconnected ping e nos packets de jogo.
/// Não é editável via zenith.yml — deve bater com o que o código realmente encode.
/// </summary>
static class ServerIdentity
{
    /// <summary>
    /// Bedrock protocol number (client <c>RequestNetworkSettings</c> / MOTD).
    /// Encode de StartGame / LevelChunk segue o layout 1001 (refs Vedrock / gophertunnel).
    /// </summary>
    public const int ProtocolVersion = 1001;

    /// <summary>String de versão de jogo no wire (StartGame / ResourcePackStack). SSOT.</summary>
    public const string VersionName = "1.26.33";
}

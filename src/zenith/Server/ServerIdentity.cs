namespace Zenith.Server;

/// <summary>
/// Identidade de protocolo anunciada no unconnected ping e nos packets de jogo.
/// Não é editável via zenith.yml — deve bater com o que o código realmente encode.
/// </summary>
static class ServerIdentity
{
    public const int ProtocolVersion = 766;

    /// <summary>String de versão de jogo no wire (StartGame / ResourcePackStack). SSOT.</summary>
    public const string VersionName = "1.26.33";
}

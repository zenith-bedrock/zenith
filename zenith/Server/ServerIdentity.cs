namespace Zenith.Server;

/// <summary>
/// Identidade de protocolo anunciada no unconnected ping.
/// Não é editável via zenith.yml — deve bater com o que o código realmente encode.
/// </summary>
static class ServerIdentity
{
    public const int ProtocolVersion = 766;
    public const string VersionName = "1.21.50";
}

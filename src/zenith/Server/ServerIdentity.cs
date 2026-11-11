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
    /// Targets stable Bedrock 1.26.60–1.26.62 (protocol 2225), the first protocol whose clients only join
    /// third-party servers over NetherNet. Wire deltas from 2193 are listed in <c>docs/protocol-churn.md</c>;
    /// this version gate alone does not imply every Bedrock packet is implemented or verified against a live client.
    /// </summary>
    public const int ProtocolVersion = 2225;

    /// <summary>
    /// Protocol the embedded block/item/creative/biome data was extracted for (<c>data/manifest.json</c>).
    /// Lags <see cref="ProtocolVersion"/> until the data is re-extracted from BDS 1.26.60+
    /// (<c>tools/bedrock-data</c>); set it to <see cref="ProtocolVersion"/> in the same change as that refresh.
    /// </summary>
    public const int EmbeddedDataProtocolVersion = 2193;

    /// <summary>Game version of the embedded data; see <see cref="EmbeddedDataProtocolVersion"/>.</summary>
    public const string EmbeddedDataVersionName = "1.26.52";

    /// <summary>String de versão de jogo no wire (StartGame / ResourcePackStack). SSOT.</summary>
    public const string VersionName = "1.26.62";
}

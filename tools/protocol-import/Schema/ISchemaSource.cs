namespace Zenith.ProtocolImport.Schema;

/// <summary>
/// Abstraction over a public protocol-dump source. The two implementations intentionally expose
/// the same cache/provenance contract despite their different upstream schema shapes.
/// </summary>
internal interface ISchemaSource : IDisposable
{
    string Name { get; }

    /// <summary>Default git ref/branch to pull when the caller doesn't specify one.</summary>
    string DefaultRef { get; }

    Task<CacheManifest> PullAsync(string cacheDir, string @ref, CancellationToken ct);

    PacketSchema? ReadPacket(string cacheDir, string packetName);

    TypeSchema? ReadType(string cacheDir, string typeName);

    IReadOnlyList<string> ListCachedPackets(string cacheDir);
}

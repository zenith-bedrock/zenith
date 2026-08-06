namespace Zenith.ProtocolImport.Schema;

/// <summary>
/// Abstraction over a public protocol-dump source. EndstoneSchemaSource is the only
/// implementation shipped in v1 (see ADR §76 future-work note) - Mojang's bedrock-protocol-docs
/// carries the same information in a more verbose JSON-Schema-with-$ref shape and can get its
/// own implementation later without touching callers.
/// </summary>
internal interface ISchemaSource : IDisposable
{
    string Name { get; }

    /// <summary>Default git ref/branch to pull when the caller doesn't specify one.</summary>
    string DefaultRef { get; }

    Task PullAsync(string cacheDir, string @ref, CancellationToken ct);

    PacketSchema? ReadPacket(string cacheDir, string packetName);

    TypeSchema? ReadType(string cacheDir, string typeName);

    IReadOnlyList<string> ListCachedPackets(string cacheDir);
}

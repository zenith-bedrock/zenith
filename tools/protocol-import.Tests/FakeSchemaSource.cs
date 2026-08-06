using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

/// <summary>In-memory ISchemaSource for scaffolder tests - no disk/network access.</summary>
internal sealed class FakeSchemaSource : ISchemaSource
{
    public Dictionary<string, TypeSchema> Types { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, PacketSchema> Packets { get; } = new(StringComparer.Ordinal);

    public string Name => "fake";
    public string DefaultRef => "fake-ref";

    public Task PullAsync(string cacheDir, string @ref, CancellationToken ct) => Task.CompletedTask;

    public PacketSchema? ReadPacket(string cacheDir, string packetName) =>
        Packets.GetValueOrDefault(packetName);

    public TypeSchema? ReadType(string cacheDir, string typeName) =>
        Types.GetValueOrDefault(typeName);

    public IReadOnlyList<string> ListCachedPackets(string cacheDir) => Packets.Keys.ToList();

    public void Dispose() { }
}

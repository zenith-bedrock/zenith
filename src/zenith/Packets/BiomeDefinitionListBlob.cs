using System.Reflection;

namespace Zenith.Packets;

/// <summary>
/// Embedded vanilla <c>BiomeDefinitionList</c> wire body (defs + string_list, no packet id).
/// Source: minecraft-data bedrock biomes encoded to protocol 1.26.x shape (ADR §70).
/// </summary>
static class BiomeDefinitionListBlob
{
    public const string EmbeddedResourceName = "biome_definitions.bin";

    private static readonly byte[] Payload = Load();

    /// <summary>Packet body after the BiomeDefinitionList id varuint.</summary>
    public static ReadOnlySpan<byte> WireBody => Payload;

    public static int ByteLength => Payload.Length;

    private static byte[] Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResourceName}' not found.");
        using var ms = new MemoryStream(capacity: (int)Math.Max(stream.Length, 64));
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (bytes.Length == 0)
            throw new InvalidOperationException($"{EmbeddedResourceName} is empty.");
        return bytes;
    }
}

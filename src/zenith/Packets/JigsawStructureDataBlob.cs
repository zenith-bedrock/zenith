using System.Reflection;
using Zenith.Nbt;

namespace Zenith.Packets;

/// <summary>Embedded vanilla Network-NBT body for JigsawStructureData (protocol 2193).</summary>
static class JigsawStructureDataBlob
{
    public const string EmbeddedResourceName = "jigsaw_structures_data.nbt";

    private static readonly byte[] Payload = Load();

    /// <summary>Named Network-NBT compound written directly after the packet id.</summary>
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

        var decoded = NbtCodec.Decode(bytes, NbtEncoding.Network);
        if (decoded.BytesConsumed != bytes.Length || !decoded.Root.Tag.TryGetCompound(out _))
            throw new InvalidOperationException(
                $"{EmbeddedResourceName} must contain exactly one Network-NBT compound.");

        return bytes;
    }
}

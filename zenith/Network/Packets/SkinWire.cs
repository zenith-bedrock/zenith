using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>Encode de skin no wire. Placeholder até parse real do login.</summary>
static class SkinWire
{
    private const uint Width = 64;
    private const uint Height = 64;
    private static readonly byte[] WhitePixels = CreateWhiteRgba();

    public static void WritePlaceholder(ref BinaryStream writer, string skinId)
    {
        writer.WriteVarString(skinId);
        writer.WriteVarString(""); // playFabId
        writer.WriteVarString("""{"geometry":{"default":"geometry.humanoid.custom"}}""");
        WriteImage(ref writer, Width, Height, WhitePixels);
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // animations
        WriteImage(ref writer, 0, 0, ReadOnlySpan<byte>.Empty); // cape
        writer.WriteVarString(""); // geometryData
        writer.WriteVarString(""); // geometryDataVersion
        writer.WriteVarString(""); // animationData
        writer.WriteVarString(""); // capeId
        writer.WriteVarString(skinId); // fullSkinId
        writer.WriteVarString("wide");
        writer.WriteVarString("#0");
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // personaPieces
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // pieceTintColors
        writer.WriteBool(false); // premium
        writer.WriteBool(false); // persona
        writer.WriteBool(false); // capeOnClassic
        writer.WriteBool(true); // isPrimaryUser
        writer.WriteBool(true); // override
    }

    private static void WriteImage(ref BinaryStream writer, uint width, uint height, ReadOnlySpan<byte> pixels)
    {
        writer.WriteUInt(width, BinaryStream.Endianess.Little);
        writer.WriteUInt(height, BinaryStream.Endianess.Little);
        writer.WriteByteArray(pixels);
    }

    private static byte[] CreateWhiteRgba()
    {
        var pixels = new byte[Width * Height * 4];
        pixels.AsSpan().Fill(0xff);
        return pixels;
    }
}

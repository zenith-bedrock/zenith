using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Encode de skin no wire. Placeholder até parse real do login.</summary>
static class SkinWire
{
    private const uint Width = 64;
    private const uint Height = 64;
    private static readonly byte[] WhitePixels = CreateWhiteRgba();

    public static void WritePlaceholder(ref BinaryStream writer, string skinId) =>
        Write(ref writer, skinId, WhitePixels, Width, Height);

    public static void Write(
        ref BinaryStream writer,
        string skinId,
        ReadOnlySpan<byte> rgba,
        uint width,
        uint height)
    {
        if (rgba.IsEmpty || width == 0 || height == 0 || rgba.Length != width * height * 4)
        {
            rgba = WhitePixels;
            width = Width;
            height = Height;
        }

        writer.WriteVarString(skinId);
        writer.WriteVarString("");
        writer.WriteVarString("""{"geometry":{"default":"geometry.humanoid.custom"}}""");
        WriteImage(ref writer, width, height, rgba);
        writer.WriteUInt(0, BinaryStream.Endianess.Little);
        WriteImage(ref writer, 0, 0, ReadOnlySpan<byte>.Empty);
        writer.WriteVarString("");
        writer.WriteVarString("");
        writer.WriteVarString("");
        writer.WriteVarString("");
        writer.WriteVarString(skinId);
        writer.WriteVarString("wide");
        writer.WriteVarString("#0");
        writer.WriteUInt(0, BinaryStream.Endianess.Little);
        writer.WriteUInt(0, BinaryStream.Endianess.Little);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteBool(true);
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

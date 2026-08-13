using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Classic RGBA / white placeholder when ClientData full parse is unavailable.</summary>
static class SkinWire
{
    private const uint Width = 64;
    private const uint Height = 64;
    private static readonly byte[] WhitePixels = CreateWhiteRgba();

    public static void WritePlaceholder(ref BinaryStream writer, string skinId, bool trusted = false) =>
        Write(ref writer, skinId, WhitePixels, Width, Height, trusted);

    public static void Write(
        ref BinaryStream writer,
        string skinId,
        ReadOnlySpan<byte> rgba,
        uint width,
        uint height,
        bool trusted = false)
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
        writer.WriteUnsignedVarInt(0);
        WriteImage(ref writer, 0, 0, ReadOnlySpan<byte>.Empty);
        writer.WriteByteArray([]);
        writer.WriteByteArray([]);
        writer.WriteByteArray([]);
        writer.WriteVarString("");
        writer.WriteVarString(skinId);
        writer.WriteByte(1);
        writer.WriteUInt(0, BinaryStream.Endianess.Big);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteBool(true);
        writer.WriteVarString(trusted ? "true" : "false");
        writer.WriteVarString("");
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

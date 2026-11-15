using Zenith.Binary;

namespace Zenith.Packets;

/// <summary>Classic RGBA / white placeholder when ClientData full parse is unavailable.</summary>
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
        byte[] rgba,
        uint width,
        uint height)
    {
        if (rgba.Length == 0 || width == 0 || height == 0 ||
            width > SerializedSkin.MaxImageEdge || height > SerializedSkin.MaxImageEdge ||
            rgba.Length != (long)width * height * 4)
        {
            rgba = WhitePixels;
            width = Width;
            height = Height;
        }

        var skin = SerializedSkin.Default with
        {
            Id = skinId,
            FullId = skinId,
            Image = new SkinImage { Width = width, Height = height, Data = rgba }
        };
        skin.Write(ref writer);
    }

    private static byte[] CreateWhiteRgba()
    {
        var pixels = new byte[Width * Height * 4];
        pixels.AsSpan().Fill(0xff);
        return pixels;
    }
}

using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SkinAnimation
{
    public SkinImage Image { get; init; }
    public uint Type { get; init; }
    public float Frames { get; init; }
    public uint Expression { get; init; }

    public static SkinAnimation Read(ref BinaryStream stream)
    {
        var image = SkinImage.Read(ref stream);
        var type = checked((uint)stream.ReadUnsignedVarInt());
        var frames = stream.ReadFloat(BinaryStream.Endianess.Little);
        var expression = checked((uint)stream.ReadUnsignedVarInt());
        return new SkinAnimation { Image = image, Type = type, Frames = frames, Expression = expression };
    }

    public void Write(ref BinaryStream writer)
    {
        Image.Write(ref writer);
        writer.WriteUnsignedVarInt(checked((int)Type));
        writer.WriteFloat(Frames, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(checked((int)Expression));
    }
}

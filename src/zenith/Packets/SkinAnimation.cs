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
        var type = stream.ReadUInt(BinaryStream.Endianess.Little);
        var frames = stream.ReadFloat(BinaryStream.Endianess.Little);
        var expression = stream.ReadUInt(BinaryStream.Endianess.Little);
        return new SkinAnimation { Image = image, Type = type, Frames = frames, Expression = expression };
    }

    public void Write(BinaryStream writer)
    {
        Image.Write(writer);
        writer.WriteUInt(Type, BinaryStream.Endianess.Little);
        writer.WriteFloat(Frames, BinaryStream.Endianess.Little);
        writer.WriteUInt(Expression, BinaryStream.Endianess.Little);
    }
}

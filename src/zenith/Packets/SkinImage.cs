using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SkinImage
{
    public uint Width { get; init; }
    public uint Height { get; init; }
    public byte[] Data { get; init; }

    public static SkinImage Read(ref BinaryStream stream)
    {
        var width = stream.ReadUInt(BinaryStream.Endianess.Little);
        var height = stream.ReadUInt(BinaryStream.Endianess.Little);
        var data = stream.ReadByteArray();
        return new SkinImage { Width = width, Height = height, Data = data };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteUInt(Width, BinaryStream.Endianess.Little);
        writer.WriteUInt(Height, BinaryStream.Endianess.Little);
        writer.WriteByteArray(Data ?? []);
    }
}

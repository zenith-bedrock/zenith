using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SkinPersonaTintPiece
{
    public string Type { get; init; }
    public string[] Colors { get; init; }

    public static SkinPersonaTintPiece Read(ref BinaryStream stream)
    {
        var type = stream.ReadVarString();
        var count = stream.ReadUInt(BinaryStream.Endianess.Little);
        var colors = new string[count];
        for (var i = 0; i < count; i++)
            colors[i] = stream.ReadVarString();
        return new SkinPersonaTintPiece { Type = type, Colors = colors };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteVarString(Type);
        var cols = Colors ?? [];
        writer.WriteUInt((uint)cols.Length, BinaryStream.Endianess.Little);
        foreach (var c in cols)
            writer.WriteVarString(c);
    }
}

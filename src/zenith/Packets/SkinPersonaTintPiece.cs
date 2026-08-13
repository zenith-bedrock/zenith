using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SkinPersonaTintPiece
{
    public string Type { get; init; }
    public string[] Colors { get; init; }

    public static SkinPersonaTintPiece Read(ref BinaryStream stream)
    {
        var type = ToLoginType(stream.ReadVarString());
        var colors = new string[4];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = SerializedSkin.FormatColor(stream.ReadUInt(BinaryStream.Endianess.Big));
        return new SkinPersonaTintPiece { Type = type, Colors = colors };
    }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(ToWireType(Type));
        var cols = Colors ?? [];
        for (var i = 0; i < 4; i++)
            writer.WriteUInt(i < cols.Length ? SerializedSkin.ParseWireColor(cols[i]) : 0, BinaryStream.Endianess.Big);
    }

    private static string ToWireType(string type) => type == "persona_hand" ? "hands" : (type ?? "").Replace("persona_", "", StringComparison.Ordinal);
    private static string ToLoginType(string type) => type == "hands" ? "persona_hand" : type == "unsupported" ? type : "persona_" + type;
}

using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SkinPersonaPiece
{
    public string PieceId { get; init; }
    public string PieceType { get; init; }
    public string PackId { get; init; }
    public bool IsDefault { get; init; }
    public string ProductId { get; init; }

    public static SkinPersonaPiece Read(ref BinaryStream stream)
    {
        var pieceId = stream.ReadVarString();
        var pieceType = FromWireType(stream.ReadUInt(BinaryStream.Endianess.Little));
        var packId = stream.ReadUuid().ToString("D");
        var isDefault = stream.ReadBool();
        var productId = stream.ReadVarString();
        return new SkinPersonaPiece
        {
            PieceId = pieceId,
            PieceType = pieceType,
            PackId = packId,
            IsDefault = isDefault,
            ProductId = productId
        };
    }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(PieceId);
        writer.WriteUInt(ToWireType(PieceType), BinaryStream.Endianess.Little);
        writer.WriteUuid(Guid.Parse(PackId));
        writer.WriteBool(IsDefault);
        writer.WriteVarString(ProductId);
    }

    private static uint ToWireType(string type) => type switch
    {
        "persona_skeleton" => 1, "persona_body" => 2, "persona_skin" => 3, "persona_bottom" => 4,
        "persona_feet" => 5, "persona_dress" => 6, "persona_top" => 7, "persona_high_pants" => 8,
        "persona_hands" or "persona_hand" => 9, "persona_outerwear" => 10, "persona_facial_hair" => 11,
        "persona_mouth" => 12, "persona_eyes" => 13, "persona_hair" => 14, "persona_hood" => 15,
        "persona_back" => 16, "persona_face_accessory" => 17, "persona_head" => 18, "persona_legs" => 19,
        "persona_left_leg" => 20, "persona_right_leg" => 21, "persona_arms" => 22, "persona_left_arm" => 23,
        "persona_right_arm" => 24, "persona_capes" => 25, "persona_classic_skin" => 26, "persona_emote" => 27,
        "persona_unsupported" => 28, _ => 0
    };

    private static string FromWireType(uint type) => type switch
    {
        1 => "persona_skeleton", 2 => "persona_body", 3 => "persona_skin", 4 => "persona_bottom",
        5 => "persona_feet", 6 => "persona_dress", 7 => "persona_top", 8 => "persona_high_pants",
        9 => "persona_hand", 10 => "persona_outerwear", 11 => "persona_facial_hair", 12 => "persona_mouth",
        13 => "persona_eyes", 14 => "persona_hair", 15 => "persona_hood", 16 => "persona_back",
        17 => "persona_face_accessory", 18 => "persona_head", 19 => "persona_legs", 20 => "persona_left_leg",
        21 => "persona_right_leg", 22 => "persona_arms", 23 => "persona_left_arm", 24 => "persona_right_arm",
        25 => "persona_capes", 26 => "persona_classic_skin", 27 => "persona_emote", 28 => "persona_unsupported",
        _ => "persona_unknown"
    };
}

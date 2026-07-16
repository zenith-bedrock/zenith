using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SerializedSkin
{
    public string Id { get; init; }
    public string PlayFabId { get; init; }
    public string ResourcePatch { get; init; }
    public SkinImage Image { get; init; }
    public SkinAnimation[] Animations { get; init; }
    public SkinImage CapeImage { get; init; }
    public string GeometryData { get; init; }
    public string GeometryVersion { get; init; }
    public string AnimationData { get; init; }
    public string CapeId { get; init; }
    public string FullId { get; init; }
    public string ArmSize { get; init; }
    public string SkinColor { get; init; }
    public SkinPersonaPiece[] PersonaPieces { get; init; }
    public SkinPersonaTintPiece[] TintPieces { get; init; }
    public bool IsPremium { get; init; }
    public bool IsPersona { get; init; }
    public bool IsPersonaCapeOnClassic { get; init; }
    public bool IsPrimaryUser { get; init; }
    public bool OverridesPlayerAppearance { get; init; }

    public static SerializedSkin Read(ref BinaryStream stream)
    {
        var id = stream.ReadVarString();
        var playFabId = stream.ReadVarString();
        var resourcePatch = stream.ReadVarString();
        var image = SkinImage.Read(ref stream);

        var animCount = stream.ReadUInt(BinaryStream.Endianess.Little);
        var animations = new SkinAnimation[animCount];
        for (var i = 0; i < animCount; i++)
            animations[i] = SkinAnimation.Read(ref stream);

        var capeImage = SkinImage.Read(ref stream);

        var geometryData = stream.ReadVarString();
        var geometryVersion = stream.ReadVarString();
        var animationData = stream.ReadVarString();
        var capeId = stream.ReadVarString();
        var fullId = stream.ReadVarString();
        var armSize = stream.ReadVarString();
        var skinColor = stream.ReadVarString();

        var personaCount = stream.ReadUInt(BinaryStream.Endianess.Little);
        var personaPieces = new SkinPersonaPiece[personaCount];
        for (var i = 0; i < personaCount; i++)
            personaPieces[i] = SkinPersonaPiece.Read(ref stream);

        var tintCount = stream.ReadUInt(BinaryStream.Endianess.Little);
        var tintPieces = new SkinPersonaTintPiece[tintCount];
        for (var i = 0; i < tintCount; i++)
            tintPieces[i] = SkinPersonaTintPiece.Read(ref stream);

        var isPremium = stream.ReadBool();
        var isPersona = stream.ReadBool();
        var isPersonaCapeOnClassic = stream.ReadBool();
        var isPrimaryUser = stream.ReadBool();
        var overridesPlayerAppearance = stream.ReadBool();

        return new SerializedSkin
        {
            Id = id,
            PlayFabId = playFabId,
            ResourcePatch = resourcePatch,
            Image = image,
            Animations = animations,
            CapeImage = capeImage,
            GeometryData = geometryData,
            GeometryVersion = geometryVersion,
            AnimationData = animationData,
            CapeId = capeId,
            FullId = fullId,
            ArmSize = armSize,
            SkinColor = skinColor,
            PersonaPieces = personaPieces,
            TintPieces = tintPieces,
            IsPremium = isPremium,
            IsPersona = isPersona,
            IsPersonaCapeOnClassic = isPersonaCapeOnClassic,
            IsPrimaryUser = isPrimaryUser,
            OverridesPlayerAppearance = overridesPlayerAppearance
        };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteVarString(Id);
        writer.WriteVarString(PlayFabId);
        writer.WriteVarString(ResourcePatch);
        Image.Write(writer);

        var anims = Animations ?? [];
        writer.WriteUInt((uint)anims.Length, BinaryStream.Endianess.Little);
        foreach (var a in anims)
            a.Write(writer);

        CapeImage.Write(writer);

        writer.WriteVarString(GeometryData);
        writer.WriteVarString(GeometryVersion);
        writer.WriteVarString(AnimationData);
        writer.WriteVarString(CapeId);
        writer.WriteVarString(FullId);
        writer.WriteVarString(ArmSize);
        writer.WriteVarString(SkinColor);

        var personas = PersonaPieces ?? [];
        writer.WriteUInt((uint)personas.Length, BinaryStream.Endianess.Little);
        foreach (var p in personas)
            p.Write(writer);

        var tints = TintPieces ?? [];
        writer.WriteUInt((uint)tints.Length, BinaryStream.Endianess.Little);
        foreach (var t in tints)
            t.Write(writer);

        writer.WriteBool(IsPremium);
        writer.WriteBool(IsPersona);
        writer.WriteBool(IsPersonaCapeOnClassic);
        writer.WriteBool(IsPrimaryUser);
        writer.WriteBool(OverridesPlayerAppearance);
    }

    public static readonly SerializedSkin Default = new()
    {
        Id = "",
        PlayFabId = "",
        ResourcePatch = "{\"geometry\":{\"default\":\"geometry.humanoid.custom\"}}",
        Image = DefaultImage,
        Animations = [],
        CapeImage = default,
        GeometryData = "",
        GeometryVersion = "",
        AnimationData = "",
        CapeId = "",
        FullId = "",
        ArmSize = "wide",
        SkinColor = "#0",
        PersonaPieces = [],
        TintPieces = [],
        IsPremium = false,
        IsPersona = false,
        IsPersonaCapeOnClassic = false,
        IsPrimaryUser = true,
        OverridesPlayerAppearance = true
    };

    private static readonly SkinImage DefaultImage = new()
    {
        Width = 64,
        Height = 64,
        Data = new byte[64 * 64 * 4]
    };
}

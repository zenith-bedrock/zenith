using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SerializedSkin
{
    /// <summary>Max skin / cape edge (classic 64, HD up to 128 is common).</summary>
    public const uint MaxImageEdge = 128;

    public const uint MaxAnimations = 8;
    public const uint MaxPersonaPieces = 64;
    public const uint MaxTintPieces = 64;
    public const uint MaxTintColors = 8;

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
        ValidateImageOrThrow(image, "skin", allowEmpty: true);

        var animCount = stream.ReadUInt(BinaryStream.Endianess.Little);
        if (animCount > MaxAnimations)
            throw new InvalidOperationException($"Skin animation count {animCount} exceeds cap {MaxAnimations}.");
        var animations = new SkinAnimation[animCount];
        for (var i = 0; i < animCount; i++)
        {
            animations[i] = SkinAnimation.Read(ref stream);
            ValidateImageOrThrow(animations[i].Image, "skin animation");
        }

        var capeImage = SkinImage.Read(ref stream);
        ValidateImageOrThrow(capeImage, "cape", allowEmpty: true);

        var geometryData = stream.ReadVarString();
        var geometryVersion = stream.ReadVarString();
        var animationData = stream.ReadVarString();
        var capeId = stream.ReadVarString();
        var fullId = stream.ReadVarString();
        var armSize = stream.ReadVarString();
        var skinColor = stream.ReadVarString();

        var personaCount = stream.ReadUInt(BinaryStream.Endianess.Little);
        if (personaCount > MaxPersonaPieces)
            throw new InvalidOperationException($"Persona piece count {personaCount} exceeds cap {MaxPersonaPieces}.");
        var personaPieces = new SkinPersonaPiece[personaCount];
        for (var i = 0; i < personaCount; i++)
            personaPieces[i] = SkinPersonaPiece.Read(ref stream);

        var tintCount = stream.ReadUInt(BinaryStream.Endianess.Little);
        if (tintCount > MaxTintPieces)
            throw new InvalidOperationException($"Tint piece count {tintCount} exceeds cap {MaxTintPieces}.");
        var tintPieces = new SkinPersonaTintPiece[tintCount];
        for (var i = 0; i < tintCount; i++)
            tintPieces[i] = SkinPersonaTintPiece.Read(ref stream, MaxTintColors);

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

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(Id);
        writer.WriteVarString(PlayFabId);
        writer.WriteVarString(ResourcePatch);
        Image.Write(ref writer);

        var anims = Animations ?? [];
        writer.WriteUInt((uint)anims.Length, BinaryStream.Endianess.Little);
        foreach (var a in anims)
            a.Write(ref writer);

        CapeImage.Write(ref writer);

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
            p.Write(ref writer);

        var tints = TintPieces ?? [];
        writer.WriteUInt((uint)tints.Length, BinaryStream.Endianess.Little);
        foreach (var t in tints)
            t.Write(ref writer);

        writer.WriteBool(IsPremium);
        writer.WriteBool(IsPersona);
        writer.WriteBool(IsPersonaCapeOnClassic);
        writer.WriteBool(IsPrimaryUser);
        writer.WriteBool(OverridesPlayerAppearance);
    }

    /// <summary>True when RGBA buffer matches dimensions (safe for PlayerList SkinWire).</summary>
    public bool TryGetClassicRgba(out byte[] rgba, out uint width, out uint height)
    {
        width = Image.Width;
        height = Image.Height;
        rgba = Image.Data ?? [];
        if (width == 0 || height == 0 || width > MaxImageEdge || height > MaxImageEdge)
            return false;
        var expected = (long)width * height * 4;
        return rgba.Length == expected;
    }

    private static void ValidateImageOrThrow(SkinImage image, string label, bool allowEmpty = false)
    {
        var data = image.Data ?? [];
        if (allowEmpty && image.Width == 0 && image.Height == 0 && data.Length == 0)
            return;
        if (image.Width > MaxImageEdge || image.Height > MaxImageEdge)
            throw new InvalidOperationException($"{label} image edge exceeds {MaxImageEdge}.");
        var expected = (long)image.Width * image.Height * 4;
        if (data.Length != expected)
            throw new InvalidOperationException($"{label} image data length {data.Length} != {expected}.");
    }

    public static readonly SerializedSkin Default = new()
    {
        Id = "",
        PlayFabId = "",
        ResourcePatch = "{\"geometry\":{\"default\":\"geometry.humanoid.custom\"}}",
        Image = new SkinImage
        {
            Width = 64,
            Height = 64,
            Data = new byte[64 * 64 * 4]
        },
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
}

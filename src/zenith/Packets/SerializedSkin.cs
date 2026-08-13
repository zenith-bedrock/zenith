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
    public bool Trusted { get; init; }
    public string ProfileHash { get; init; }

    public static SerializedSkin Read(ref BinaryStream stream)
    {
        var id = stream.ReadVarString();
        var playFabId = stream.ReadVarString();
        var resourcePatch = stream.ReadVarString();
        var image = SkinImage.Read(ref stream);
        ValidateImageOrThrow(image, "skin", allowEmpty: true);

        var animCount = stream.ReadUnsignedVarInt();
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

        var geometryData = System.Text.Encoding.UTF8.GetString(stream.ReadByteArray());
        var geometryVersion = System.Text.Encoding.UTF8.GetString(stream.ReadByteArray());
        var animationData = System.Text.Encoding.UTF8.GetString(stream.ReadByteArray());
        var capeId = stream.ReadVarString();
        var fullId = stream.ReadVarString();
        var armSize = stream.ReadByte() == 0 ? "slim" : "wide";
        var skinColor = FormatColor(stream.ReadUInt(BinaryStream.Endianess.Big));

        var personaCount = stream.ReadUnsignedVarInt();
        if (personaCount > MaxPersonaPieces)
            throw new InvalidOperationException($"Persona piece count {personaCount} exceeds cap {MaxPersonaPieces}.");
        var personaPieces = new SkinPersonaPiece[personaCount];
        for (var i = 0; i < personaCount; i++)
            personaPieces[i] = SkinPersonaPiece.Read(ref stream);

        var tintCount = stream.ReadUnsignedVarInt();
        if (tintCount > MaxTintPieces)
            throw new InvalidOperationException($"Tint piece count {tintCount} exceeds cap {MaxTintPieces}.");
        var tintPieces = new SkinPersonaTintPiece[tintCount];
        for (var i = 0; i < tintCount; i++)
            tintPieces[i] = SkinPersonaTintPiece.Read(ref stream);

        var isPremium = stream.ReadBool();
        var isPersona = stream.ReadBool();
        var isPersonaCapeOnClassic = stream.ReadBool();
        var isPrimaryUser = stream.ReadBool();
        var overridesPlayerAppearance = stream.ReadBool();
        var trusted = string.Equals(stream.ReadVarString(), "true", StringComparison.OrdinalIgnoreCase);
        var profileHash = stream.ReadVarString();

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
            OverridesPlayerAppearance = overridesPlayerAppearance,
            Trusted = trusted,
            ProfileHash = profileHash
        };
    }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(Id);
        writer.WriteVarString(PlayFabId);
        writer.WriteVarString(ResourcePatch);
        Image.Write(ref writer);

        var anims = Animations ?? [];
        writer.WriteUnsignedVarInt(anims.Length);
        foreach (var a in anims)
            a.Write(ref writer);

        CapeImage.Write(ref writer);

        writer.WriteByteArray(System.Text.Encoding.UTF8.GetBytes(GeometryData ?? ""));
        writer.WriteByteArray(System.Text.Encoding.UTF8.GetBytes(GeometryVersion ?? ""));
        writer.WriteByteArray(System.Text.Encoding.UTF8.GetBytes(AnimationData ?? ""));
        writer.WriteVarString(CapeId);
        writer.WriteVarString(FullId);
        writer.WriteByte(string.Equals(ArmSize, "slim", StringComparison.OrdinalIgnoreCase) ? (byte)0 : (byte)1);
        writer.WriteUInt(ParseWireColor(SkinColor), BinaryStream.Endianess.Big);

        var personas = PersonaPieces ?? [];
        writer.WriteUnsignedVarInt(personas.Length);
        foreach (var p in personas)
            p.Write(ref writer);

        var tints = TintPieces ?? [];
        writer.WriteUnsignedVarInt(tints.Length);
        foreach (var t in tints)
            t.Write(ref writer);

        writer.WriteBool(IsPremium);
        writer.WriteBool(IsPersona);
        writer.WriteBool(IsPersonaCapeOnClassic);
        writer.WriteBool(IsPrimaryUser);
        writer.WriteBool(OverridesPlayerAppearance);
        writer.WriteVarString(Trusted ? "true" : "false");
        writer.WriteVarString(ProfileHash ?? "");
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

    internal static uint ParseWireColor(string value)
    {
        var hex = (value ?? "").TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return PackWireColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);
        if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgba))
            return PackWireColor((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        return 0;
    }

    internal static string FormatColor(uint wire)
    {
        var a = (byte)wire;
        var r = (byte)(wire >> 8);
        var g = (byte)(wire >> 16);
        var b = (byte)(wire >> 24);
        return a == 0xff ? $"#{r:x2}{g:x2}{b:x2}" : $"#{r:x2}{g:x2}{b:x2}{a:x2}";
    }

    private static uint PackWireColor(byte r, byte g, byte b, byte a) =>
        (uint)(a | (r << 8) | (g << 16) | (b << 24));

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
        SkinColor = "#000000",
        PersonaPieces = [],
        TintPieces = [],
        IsPremium = false,
        IsPersona = false,
        IsPersonaCapeOnClassic = false,
        IsPrimaryUser = true,
        OverridesPlayerAppearance = true,
        Trusted = false,
        ProfileHash = ""
    };
}

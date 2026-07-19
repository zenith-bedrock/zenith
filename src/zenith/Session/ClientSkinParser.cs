using System.Text;
using System.Text.Json;
using Zenith.Packets;

namespace Zenith.Session;

/// <summary>
/// ClientData JWT → <see cref="SerializedSkin"/> (join path). Mirrors Dragonfly
/// <c>parseSkin</c> / PocketMine <c>ClientDataToSkinDataHelper</c>: full geometry,
/// cape, animations, persona pieces — not only classic RGBA.
/// </summary>
static class ClientSkinParser
{
    /// <summary>
    /// Parses ClientData JWT into a wire-ready skin. Returns false when SkinData is
    /// missing or dimensions do not match (caller keeps placeholder).
    /// </summary>
    public static bool TryParse(string clientDataJwt, out SerializedSkin skin)
    {
        skin = default;
        try
        {
            using var payload = ReadPayload(clientDataJwt);
            var root = payload.RootElement;
            if (!TryDecodeBytes(root, "SkinData", out var skinData) || skinData.Length == 0)
                return false;

            uint width = 64, height = 64;
            if (root.TryGetProperty("SkinImageWidth", out var ww) && ww.TryGetUInt32(out var w))
                width = w;
            if (root.TryGetProperty("SkinImageHeight", out var hh) && hh.TryGetUInt32(out var h))
                height = h;

            if (width == 0 || height == 0 || width > SerializedSkin.MaxImageEdge || height > SerializedSkin.MaxImageEdge)
                return false;
            if (skinData.Length != (long)width * height * 4)
                return false;

            var skinId = ReadString(root, "SkinId") ?? "";
            var playFabId = ReadString(root, "PlayFabId") ?? "";
            var resourcePatch = TryDecodeUtf8(root, "SkinResourcePatch")
                ?? """{"geometry":{"default":"geometry.humanoid.custom"}}""";
            var geometryData = TryDecodeUtf8(root, "SkinGeometryData")
                ?? TryDecodeUtf8(root, "SkinGeometry")
                ?? "";
            var geometryVersion = TryDecodeUtf8(root, "SkinGeometryDataEngineVersion") ?? "";
            var animationData = TryDecodeUtf8(root, "SkinAnimationData") ?? "";
            var capeId = ReadString(root, "CapeId") ?? "";
            var armSize = ReadString(root, "ArmSize") ?? "wide";
            var skinColor = ReadString(root, "SkinColor") ?? "#0";

            var cape = ReadCape(root);
            var animations = ReadAnimations(root);
            var personaPieces = ReadPersonaPieces(root);
            var tintPieces = ReadTintPieces(root);

            skin = new SerializedSkin
            {
                Id = skinId,
                PlayFabId = playFabId,
                ResourcePatch = resourcePatch,
                Image = new SkinImage { Width = width, Height = height, Data = skinData },
                Animations = animations,
                CapeImage = cape,
                GeometryData = geometryData,
                GeometryVersion = geometryVersion,
                AnimationData = animationData,
                CapeId = capeId,
                FullId = string.IsNullOrEmpty(skinId) ? Guid.NewGuid().ToString("D") : skinId,
                ArmSize = armSize,
                SkinColor = skinColor,
                PersonaPieces = personaPieces,
                TintPieces = tintPieces,
                IsPremium = ReadBool(root, "PremiumSkin"),
                IsPersona = ReadBool(root, "PersonaSkin"),
                IsPersonaCapeOnClassic = ReadBool(root, "CapeOnClassicSkin"),
                IsPrimaryUser = true,
                // PocketMine: absent OverrideSkin → true
                OverridesPlayerAppearance = !root.TryGetProperty("OverrideSkin", out var ov)
                    || ov.ValueKind != JsonValueKind.False
            };

            return true;
        }
        catch
        {
            skin = default;
            return false;
        }
    }

    public static bool IsTrusted(string clientDataJwt)
    {
        try
        {
            using var payload = ReadPayload(clientDataJwt);
            return ReadBool(payload.RootElement, "TrustedSkin");
        }
        catch
        {
            return false;
        }
    }

    private static SkinImage ReadCape(JsonElement root)
    {
        if (!TryDecodeBytes(root, "CapeData", out var capeData) || capeData.Length == 0)
            return default;

        uint width = 0, height = 0;
        if (root.TryGetProperty("CapeImageWidth", out var ww) && ww.TryGetUInt32(out var w))
            width = w;
        if (root.TryGetProperty("CapeImageHeight", out var hh) && hh.TryGetUInt32(out var h))
            height = h;

        if (width == 0 || height == 0 || width > SerializedSkin.MaxImageEdge || height > SerializedSkin.MaxImageEdge)
            return default;
        if (capeData.Length != (long)width * height * 4)
            return default;

        return new SkinImage { Width = width, Height = height, Data = capeData };
    }

    private static SkinAnimation[] ReadAnimations(JsonElement root)
    {
        if (!root.TryGetProperty("AnimatedImageData", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<SkinAnimation>();
        foreach (var el in arr.EnumerateArray())
        {
            if (list.Count >= SerializedSkin.MaxAnimations) break;
            if (!TryDecodeBytes(el, "Image", out var pixels) || pixels.Length == 0) continue;

            uint width = 0, height = 0;
            if (el.TryGetProperty("ImageWidth", out var ww) && ww.TryGetUInt32(out var w))
                width = w;
            if (el.TryGetProperty("ImageHeight", out var hh) && hh.TryGetUInt32(out var h))
                height = h;
            if (width == 0 || height == 0 || width > SerializedSkin.MaxImageEdge || height > SerializedSkin.MaxImageEdge)
                continue;
            if (pixels.Length != (long)width * height * 4) continue;

            uint type = 0;
            if (el.TryGetProperty("Type", out var t) && t.TryGetUInt32(out var tv))
                type = tv;
            float frames = 0;
            if (el.TryGetProperty("Frames", out var f))
            {
                if (f.TryGetSingle(out var fs)) frames = fs;
                else if (f.TryGetDouble(out var fd)) frames = (float)fd;
            }

            uint expression = 0;
            if (el.TryGetProperty("AnimationExpression", out var ex) && ex.TryGetUInt32(out var ev))
                expression = ev;

            list.Add(new SkinAnimation
            {
                Image = new SkinImage { Width = width, Height = height, Data = pixels },
                Type = type,
                Frames = frames,
                Expression = expression
            });
        }

        return list.ToArray();
    }

    private static SkinPersonaPiece[] ReadPersonaPieces(JsonElement root)
    {
        if (!root.TryGetProperty("PersonaPieces", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<SkinPersonaPiece>();
        foreach (var el in arr.EnumerateArray())
        {
            if (list.Count >= SerializedSkin.MaxPersonaPieces) break;
            var pieceId = ReadString(el, "PieceId") ?? "";
            var pieceType = ReadString(el, "PieceType") ?? "";
            var packId = ReadString(el, "PackId") ?? Guid.Empty.ToString("D");
            if (!Guid.TryParse(packId, out _))
                packId = Guid.Empty.ToString("D");
            var productId = ReadString(el, "ProductId") ?? "";
            var isDefault = ReadBool(el, "IsDefault");
            list.Add(new SkinPersonaPiece
            {
                PieceId = pieceId,
                PieceType = pieceType,
                PackId = packId,
                IsDefault = isDefault,
                ProductId = productId
            });
        }

        return list.ToArray();
    }

    private static SkinPersonaTintPiece[] ReadTintPieces(JsonElement root)
    {
        if (!root.TryGetProperty("PieceTintColors", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<SkinPersonaTintPiece>();
        foreach (var el in arr.EnumerateArray())
        {
            if (list.Count >= SerializedSkin.MaxTintPieces) break;
            var type = ReadString(el, "PieceType") ?? "";
            var colors = Array.Empty<string>();
            if (el.TryGetProperty("Colors", out var cols) && cols.ValueKind == JsonValueKind.Array)
            {
                var tmp = new List<string>();
                foreach (var c in cols.EnumerateArray())
                {
                    if (tmp.Count >= SerializedSkin.MaxTintColors) break;
                    if (c.ValueKind == JsonValueKind.String && c.GetString() is { } s)
                        tmp.Add(s);
                }

                colors = tmp.ToArray();
            }

            list.Add(new SkinPersonaTintPiece { Type = type, Colors = colors });
        }

        return list.ToArray();
    }

    private static JsonDocument ReadPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            throw new FormatException("JWT missing payload.");
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(parts[1].Replace('-', '+').Replace('_', '/'))));
        return JsonDocument.Parse(json);
    }

    private static bool TryDecodeBytes(JsonElement root, string name, out byte[] bytes)
    {
        bytes = [];
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            return false;
        var b64 = el.GetString();
        if (string.IsNullOrWhiteSpace(b64))
            return false;
        try
        {
            bytes = Convert.FromBase64String(PadBase64(b64.Replace('-', '+').Replace('_', '/')));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryDecodeUtf8(JsonElement root, string name)
    {
        if (!TryDecodeBytes(root, name, out var bytes))
            return null;
        return Encoding.UTF8.GetString(bytes);
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        return el.GetString();
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false
        };
    }

    private static string PadBase64(string input)
    {
        var pad = (4 - input.Length % 4) % 4;
        return pad == 0 ? input : input + new string('=', pad);
    }
}

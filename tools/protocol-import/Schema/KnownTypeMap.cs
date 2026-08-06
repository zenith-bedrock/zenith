namespace Zenith.ProtocolImport.Schema;

internal enum WireEmissionKind
{
    /// <summary>[Wire] with the given C# type - fixed-width numeric/bool.</summary>
    Fixed,

    /// <summary>[WireVar] - LEB128/zigzag family.</summary>
    Var,

    /// <summary>[WireString].</summary>
    String,

    /// <summary>[WireUuid] Guid.</summary>
    Uuid,

    /// <summary>[WireByteArray] byte[].</summary>
    ByteArray,

    /// <summary>Type isn't in the known-primitive table - caller must try nested-type resolution.</summary>
    Unknown
}

internal sealed record WireEmission(WireEmissionKind Kind, string ClrType, string? Endianess = null);

/// <summary>
/// Hardcoded mapping from Endstone's `type` field names to the [Wire*] attribute vocabulary
/// (src/zenith/Packets/Generation/WireAttributes.cs). Deliberately not exhaustive - see ADR §76
/// v1 scope. Extend this table as more packets get scaffolded and new type names show up.
/// </summary>
internal static class KnownTypeMap
{
    private static readonly Dictionary<string, WireEmission> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bool"] = new WireEmission(WireEmissionKind.Fixed, "bool"),
        ["uint8"] = new WireEmission(WireEmissionKind.Fixed, "byte"),
        ["int16"] = new WireEmission(WireEmissionKind.Fixed, "short", "Little"),
        ["uint16"] = new WireEmission(WireEmissionKind.Fixed, "ushort", "Little"),
        ["int32"] = new WireEmission(WireEmissionKind.Fixed, "int", "Little"),
        ["uint32"] = new WireEmission(WireEmissionKind.Fixed, "uint", "Little"),
        ["int64"] = new WireEmission(WireEmissionKind.Fixed, "long", "Little"),
        ["uint64"] = new WireEmission(WireEmissionKind.Fixed, "ulong", "Little"),
        ["float"] = new WireEmission(WireEmissionKind.Fixed, "float", "Little"),
        ["double"] = new WireEmission(WireEmissionKind.Fixed, "double", "Little"),

        ["varint32"] = new WireEmission(WireEmissionKind.Var, "int"),
        ["uvarint32"] = new WireEmission(WireEmissionKind.Var, "uint"),
        ["varint64"] = new WireEmission(WireEmissionKind.Var, "long"),
        ["uvarint64"] = new WireEmission(WireEmissionKind.Var, "ulong"),

        ["string"] = new WireEmission(WireEmissionKind.String, "string"),
        ["mce::uuid"] = new WireEmission(WireEmissionKind.Uuid, "Guid"),

        // Raw length-prefixed blob (not UTF-8). Not confirmed against a real packet yet -
        // these keys are a best guess at likely spellings; extend/correct once a real source
        // field surfaces one (WireEmissionKind.ByteArray was otherwise unreachable dead code).
        ["bytes"] = new WireEmission(WireEmissionKind.ByteArray, "byte[]"),
        ["buffer"] = new WireEmission(WireEmissionKind.ByteArray, "byte[]"),

        // Known IDs that are wire-varints under the hood, confirmed against the real Mojang
        // schema's "Compression" serialization option for ActorRuntimeID (see ADR §76 research).
        ["actorruntimeid"] = new WireEmission(WireEmissionKind.Var, "ulong"),
        ["actoruniqueid"] = new WireEmission(WireEmissionKind.Var, "long"),
        ["playerinputtick"] = new WireEmission(WireEmissionKind.Var, "ulong"),
    };

    public static WireEmission Resolve(string endstoneType) =>
        Map.TryGetValue(endstoneType, out var emission)
            ? emission
            : new WireEmission(WireEmissionKind.Unknown, endstoneType);
}

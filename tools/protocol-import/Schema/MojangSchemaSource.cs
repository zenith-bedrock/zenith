using System.Text.Json;

namespace Zenith.ProtocolImport.Schema;

/// <summary>
/// Pulls from Mojang/bedrock-protocol-docs (the official source). Packets are split across
/// two JSON-Schema (draft-07) files - "{Name}.json" (id + $ref to the payload) and
/// "{Name}Payload.json" (the actual field list) - with nested $ref chasing for enums,
/// single-scalar ID wrappers (e.g. ActorRuntimeID), and real nested types. See ADR §76
/// addendum for the two sharp edges this parser exists to handle correctly:
/// (1) field order is "x-ordinal-index", not JSON property declaration order;
/// (2) "required" only means wire-optional at the packet-payload level - on a standalone
/// type file it just means "has a JSON-Schema default value", not "has a has-value-flag".
/// </summary>
internal sealed class MojangSchemaSource : ISchemaSource
{
    public const string DefaultRefConst = "main";
    private const string JsonDir = "json";

    private readonly ISchemaRepository _repository;

    public MojangSchemaSource(string? localRepository = null) =>
        _repository = localRepository is null
            ? new GitHubContentClient("Mojang", "bedrock-protocol-docs")
            : new LocalGitSchemaRepository(localRepository);

    internal MojangSchemaSource(ISchemaRepository repository) => _repository = repository;

    public string Name => "mojang";
    public string DefaultRef => DefaultRefConst;

    /// <summary>Cache root for this source, namespaced under the shared --cache dir so
    /// filenames that collide across providers (both have "AnimatePacket.json") don't clobber
    /// each other.</summary>
    private static string Root(string cacheDir) => SchemaCache.GetReadRoot(cacheDir, "mojang");

    public async Task<CacheManifest> PullAsync(string cacheDir, string @ref, CancellationToken ct)
    {
        var sha = await _repository.ResolveCommitShaAsync(@ref, ct);
        var listing = await _repository.ListFilesAsync(JsonDir, sha, ct);
        return await SchemaCache.PublishAsync(cacheDir, Name, @ref, sha, async (staging, token) =>
        {
            foreach (var entry in listing.Where(e => e.Type == "file" && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var content = await _repository.ReadFileAsync($"{JsonDir}/{entry.Name}", sha, token);
                await File.WriteAllTextAsync(Path.Combine(staging, entry.Name), content, token);
            }
        }, ct);
    }

    public PacketSchema? ReadPacket(string cacheDir, string packetName)
    {
        var outerPath = Path.Combine(Root(cacheDir), $"{packetName}.json");
        if (!File.Exists(outerPath)) return null;

        using var outerDoc = JsonDocument.Parse(File.ReadAllText(outerPath));
        var outerRoot = outerDoc.RootElement;

        var id = 0;
        if (outerRoot.TryGetProperty("$metaProperties", out var meta) &&
            meta.TryGetProperty("[cereal:packet]", out var idEl))
        {
            id = idEl.GetInt32();
        }

        var payloadName = ResolveRefFileName(outerRoot) ?? $"{packetName}Payload";
        var payloadPath = Path.Combine(Root(cacheDir), $"{payloadName}.json");
        if (!File.Exists(payloadPath)) return new PacketSchema(id, packetName, []);

        using var payloadDoc = JsonDocument.Parse(File.ReadAllText(payloadPath));
        var fields = ParseProperties(payloadDoc.RootElement, cacheDir, isPacketPayload: true);

        return new PacketSchema(id, packetName, fields);
    }

    public TypeSchema? ReadType(string cacheDir, string typeName)
    {
        var path = Path.Combine(Root(cacheDir), $"{typeName}.json");
        if (!File.Exists(path)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var fields = ParseProperties(doc.RootElement, cacheDir, isPacketPayload: false);
        return new TypeSchema(typeName, fields);
    }

    public IReadOnlyList<string> ListCachedPackets(string cacheDir)
    {
        var dir = Root(cacheDir);
        if (!Directory.Exists(dir)) return [];

        var result = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("$ref", out _) &&
                    root.TryGetProperty("$metaProperties", out var meta) &&
                    meta.ValueKind == JsonValueKind.Object &&
                    meta.TryGetProperty("[cereal:packet]", out _))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name is not null) result.Add(name);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // The 900+ cached files span packets/payloads/types/enums with different
                // top-level shapes (some are bare arrays, some objects without these keys) -
                // skip anything that doesn't match rather than fail the whole listing.
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static string? ResolveRefFileName(JsonElement root)
    {
        if (!root.TryGetProperty("$ref", out var refEl)) return null;
        var raw = refEl.GetString();
        if (raw is null) return null;

        var name = raw.TrimStart('.', '/');
        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? name[..^".json".Length]
            : name;
    }

    /// <summary>
    /// Parses a "type":"object","properties":{...} shape (identical between packet payloads and
    /// standalone type files) into ordinal-index-sorted fields. See the class doc comment for
    /// the isPacketPayload / "required" asymmetry this method enforces.
    /// </summary>
    private List<FieldSchema> ParseProperties(JsonElement root, string cacheDir, bool isPacketPayload)
    {
        var result = new List<FieldSchema>();
        if (!root.TryGetProperty("properties", out var propsEl) || propsEl.ValueKind != JsonValueKind.Object)
            return result;

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (isPacketPayload && root.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in reqEl.EnumerateArray())
            {
                var s = r.GetString();
                if (s is not null) required.Add(s);
            }
        }

        var entries = new List<(int Ordinal, string Name, JsonElement Value)>();
        foreach (var prop in propsEl.EnumerateObject())
        {
            var ordinal = prop.Value.TryGetProperty("x-ordinal-index", out var ordEl) ? ordEl.GetInt32() : int.MaxValue;
            entries.Add((ordinal, prop.Name, prop.Value));
        }

        // OrderBy (stable), not List<T>.Sort (not guaranteed stable) - fields missing
        // "x-ordinal-index" all tie at int.MaxValue and must keep JSON declaration order
        // relative to each other instead of shuffling between runs.
        var sorted = entries.OrderBy(e => e.Ordinal).ToList();

        foreach (var (_, name, value) in sorted)
        {
            // isPacketPayload guards the whole "required drives optionality" rule - on a type
            // file every field is always-present regardless of what "required" says there.
            var optional = isPacketPayload && !required.Contains(name);
            result.Add(ResolveField(name, value, cacheDir, optional));
        }

        return result;
    }

    private FieldSchema ResolveField(string name, JsonElement value, string cacheDir, bool optional)
    {
        if (value.TryGetProperty("oneOf", out _) || value.TryGetProperty("anyOf", out _))
            return new FieldSchema(name, "", null, optional, null, IsComplexType: true,
                Construct: SchemaConstruct.Union, UnsupportedReason: "union discriminator not supported");

        if (value.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String &&
            typeEl.GetString() == "array")
        {
            var elementType = value.TryGetProperty("items", out var itemsEl)
                ? ResolveScalarOrRefTypeName(itemsEl, cacheDir)
                : "<unknown>";

            // Mojang's schema has no count-encoding equivalent to Endstone's "repeat.prefix" -
            // RepeatPrefix carries a non-null sentinel just to signal "this is an array" to the
            // scaffolder; the actual encoding falls back to UnsignedVarInt there, with a note.
            return new FieldSchema(name, elementType, null, optional, RepeatPrefix: "<unspecified>",
                Construct: SchemaConstruct.Array,
                Reference: itemsEl.ValueKind == JsonValueKind.Object && itemsEl.TryGetProperty("$ref", out var itemRef) ? itemRef.GetString() : null,
                UnsupportedReason: "array count encoding is not represented by the Mojang schema");
        }

        if (!value.TryGetProperty("type", out _) && !value.TryGetProperty("$ref", out _))
            return new FieldSchema(name, "", null, optional, null, Construct: SchemaConstruct.Unknown,
                UnsupportedReason: "schema field has no scalar type or reference");

        return new FieldSchema(name, ResolveScalarOrRefTypeName(value, cacheDir), null, optional, null,
            Reference: value.TryGetProperty("$ref", out var reference) ? reference.GetString() : null);
    }

    /// <summary>Max $ref hops ResolveScalarOrRefTypeName will chase through single-scalar
    /// wrappers before giving up and treating the ref as a genuine nested type - guards against
    /// a pathological/self-referential chain in the source data turning into infinite recursion.
    /// Every real chain seen during research was 1 hop (e.g. RuntimeId -> ActorRuntimeID -> uint64).</summary>
    private const int MaxWrapperDepth = 5;

    /// <summary>
    /// Resolves a property (or array "items") to a KnownTypeMap-compatible type key. Handles
    /// the three $ref shapes found during research: enum (flattens to its underlying scalar),
    /// single-scalar ID wrapper like ActorRuntimeID (flattens to the wrapped scalar - chased
    /// recursively, since the lone property can itself be another wrapper $ref), and a genuine
    /// multi-property nested type (returns the bare type name for TryResolveNestedType).
    /// </summary>
    private string ResolveScalarOrRefTypeName(JsonElement propEl, string cacheDir, int depth = 0)
    {
        if (!propEl.TryGetProperty("$ref", out _))
        {
            var jsonType = propEl.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var underlying = propEl.TryGetProperty("x-underlying-type", out var u) ? u.GetString() : null;
            return MojangTypeToKey(jsonType, underlying, HasCompression(propEl));
        }

        var refName = ResolveRefFileName(propEl) ?? "";
        if (IsUuidRef(refName)) return "mce::uuid";

        var refPath = Path.Combine(Root(cacheDir), $"{refName}.json");
        if (!File.Exists(refPath)) return refName; // unresolved - flows through as Unknown, scaffolder emits a TODO.

        using var refDoc = JsonDocument.Parse(File.ReadAllText(refPath));
        var refRoot = refDoc.RootElement;

        // Enum: "type":"string","enum":[...],"x-underlying-type":"uint8" (+ Enum-as-Value).
        if (refRoot.TryGetProperty("enum", out _) && refRoot.TryGetProperty("x-underlying-type", out var enumUnderlying))
        {
            return MojangTypeToKey("integer", enumUnderlying.GetString(), HasCompression(refRoot));
        }

        // Single-scalar ID wrapper (e.g. ActorRuntimeID): one property, which may itself be a
        // plain scalar OR another wrapper/enum $ref (e.g. a hypothetical wrapper-around-a-wrapper)
        // - recurse either way, bounded by MaxWrapperDepth.
        if (refRoot.TryGetProperty("type", out var rt) && rt.ValueKind == JsonValueKind.String && rt.GetString() == "object" &&
            refRoot.TryGetProperty("properties", out var refProps) && refProps.ValueKind == JsonValueKind.Object)
        {
            var propList = refProps.EnumerateObject().ToList();
            if (propList.Count == 1 && depth < MaxWrapperDepth)
                return ResolveScalarOrRefTypeName(propList[0].Value, cacheDir, depth + 1);
        }

        // Genuine multi-property nested type, or a wrapper chain that hit MaxWrapperDepth -
        // bare name, resolved later via ReadType (which will itself fail gracefully if this
        // is actually an unresolvable wrapper rather than a real nested type).
        return refName;
    }

    private static string MojangTypeToKey(string jsonType, string? underlyingType, bool hasCompression)
    {
        if (jsonType == "string") return "string";
        if (jsonType == "boolean") return "bool";

        var u = underlyingType ?? jsonType;
        if (!hasCompression) return u;

        return u switch
        {
            "int32" => "varint32",
            "uint32" => "uvarint32",
            "int64" => "varint64",
            "uint64" => "uvarint64",
            _ => u
        };
    }

    private static bool HasCompression(JsonElement el)
    {
        if (!el.TryGetProperty("x-serialization-options", out var opts) || opts.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var o in opts.EnumerateArray())
            if (o.GetString() == "Compression") return true;

        return false;
    }

    private static bool IsUuidRef(string refName) =>
        refName.Replace("_", "").Replace(":", "").Equals("mceuuid", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _repository.Dispose();
}

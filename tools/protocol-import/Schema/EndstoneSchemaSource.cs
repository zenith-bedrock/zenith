using System.Text.Json;

namespace Zenith.ProtocolImport.Schema;

/// <summary>
/// Pulls from EndstoneMC/protocol-docs (github.com/EndstoneMC/protocol-docs), the only schema
/// source supported in v1 - see ADR §76. Confirmed default branch is "r26_u4", not "main".
/// </summary>
internal sealed class EndstoneSchemaSource : ISchemaSource, IDisposable
{
    /// <summary>Kept as a compile-time const (not just the interface property) so
    /// [Description] attributes elsewhere can still reference it.</summary>
    public const string DefaultRefConst = "r26_u4";

    private readonly GitHubContentClient _client = new("EndstoneMC", "protocol-docs");

    public string Name => "endstone";
    public string DefaultRef => DefaultRefConst;

    /// <summary>Cache root for this source, namespaced under the shared --cache dir so
    /// filenames that collide across providers (both have "AnimatePacket.json") don't clobber
    /// each other.</summary>
    private static string Root(string cacheDir) => SchemaCache.GetReadRoot(cacheDir, "endstone");

    public async Task<CacheManifest> PullAsync(string cacheDir, string @ref, CancellationToken ct)
    {
        var sha = await _client.ResolveCommitShaAsync(@ref, ct);
        var folders = new[] { "packets", "types", "enums" };
        var listings = new Dictionary<string, IReadOnlyList<GitHubContentClient.GitHubEntry>>();
        foreach (var folder in folders) listings[folder] = await _client.ListFilesAsync(folder, sha, ct);
        return await SchemaCache.PublishAsync(cacheDir, Name, @ref, sha, async (staging, token) =>
        {
            foreach (var folder in folders)
            foreach (var entry in listings[folder].Where(e => e.Type == "file" && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var content = await _client.FetchRawAsync(folder, entry.Name, sha, token);
                var destDir = Path.Combine(staging, folder);
                Directory.CreateDirectory(destDir);
                await File.WriteAllTextAsync(Path.Combine(destDir, entry.Name), content, token);
            }
        }, ct);
    }

    public PacketSchema? ReadPacket(string cacheDir, string packetName)
    {
        var path = Path.Combine(Root(cacheDir), "packets", $"{packetName}.json");
        if (!File.Exists(path)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? packetName : packetName;
        var fields = ParseFields(root);

        return new PacketSchema(id, name, fields);
    }

    public TypeSchema? ReadType(string cacheDir, string typeName)
    {
        var path = Path.Combine(Root(cacheDir), "types", $"{typeName}.json");
        if (!File.Exists(path)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new TypeSchema(typeName, ParseFields(root));
    }

    public IReadOnlyList<string> ListCachedPackets(string cacheDir)
    {
        var dir = Path.Combine(Root(cacheDir), "packets");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private static List<FieldSchema> ParseFields(JsonElement root)
    {
        var result = new List<FieldSchema>();
        if (!root.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var f in fieldsEl.EnumerateArray())
        {
            var name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            // "type" is usually a plain string, but discriminated-union fields (e.g.
            // InventoryTransactionPacket's "Transaction") express it as a {"switch":..,"cases":[..]}
            // object instead. Treat that as an unresolvable complex type rather than crashing -
            // it flows through as WireEmissionKind.Unknown and gets a TODO comment, same as any
            // other type KnownTypeMap doesn't recognize.
            var isComplexType = false;
            string type;
            if (f.TryGetProperty("type", out var t))
            {
                if (t.ValueKind == JsonValueKind.String)
                {
                    type = t.GetString() ?? "";
                }
                else
                {
                    type = "<complex-union>";
                    isComplexType = true;
                }
            }
            else
            {
                type = "";
            }

            var enumName = f.TryGetProperty("enum", out var e) ? e.GetString() : null;
            var optional = f.TryGetProperty("optional", out var o) && o.ValueKind == JsonValueKind.True;

            // A field with a fixed "value" and (often) no "name" is a wire-present literal/tag,
            // not a real settable property - same category as StartGamePacket's hardcoded
            // placeholders in the hand-written packets (ADR §76). Scaffolding it as a bogus
            // "Field" property would risk duplicate-name collisions across multiple such markers.
            var isConstantLiteral = f.TryGetProperty("value", out _);

            string? repeatPrefix = null;
            if (f.TryGetProperty("repeat", out var r) && r.ValueKind == JsonValueKind.Object &&
                r.TryGetProperty("prefix", out var p))
            {
                repeatPrefix = p.GetString();
            }

            var construct = isConstantLiteral ? SchemaConstruct.Constant : isComplexType ? SchemaConstruct.Union :
                repeatPrefix is not null ? SchemaConstruct.Array : SchemaConstruct.Scalar;
            var reason = isComplexType ? "discriminated union not supported" : null;
            result.Add(new FieldSchema(name, type, enumName, optional, repeatPrefix, isConstantLiteral, isComplexType,
                construct, enumName, reason));
        }

        return result;
    }

    public void Dispose() => _client.Dispose();
}

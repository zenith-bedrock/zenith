using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Zenith.Packets;

/// <summary>
/// The join-time packets of a stock BDS that carry no per-player or per-world data (entity identifiers, entity property
/// sync, voxel shapes, trim data, camera presets), embedded by <c>bedrock-data join-static</c>. The manifest records the
/// BDS build they were captured from; every body is checked against its recorded hash at load, and the whole set must
/// have been captured for the protocol this server speaks.
/// </summary>
static class JoinStaticData
{
    public const string ManifestResourceName = "join_static.json";

    /// <summary>One captured packet: the packet id and everything after its header.</summary>
    internal readonly record struct Entry(string Name, int Id, byte[] Body);

    private static readonly Lazy<(string BedrockVersion, int Protocol, IReadOnlyList<Entry> Entries)> Loaded = new(Load);

    public static string BedrockVersion => Loaded.Value.BedrockVersion;

    public static int Protocol => Loaded.Value.Protocol;

    public static IReadOnlyList<Entry> All => Loaded.Value.Entries;

    /// <summary>The packets captured under <paramref name="name"/>, in the order BDS sent them (there can be several).</summary>
    public static IEnumerable<StaticDataPacket> Packets(string name)
    {
        var found = false;
        foreach (var entry in All)
        {
            if (entry.Name != name) continue;
            found = true;
            yield return new StaticDataPacket(entry.Id, entry.Body);
        }

        if (!found) throw new InvalidOperationException($"No static join packet '{name}' is embedded.");
    }

    private static (string, int, IReadOnlyList<Entry>) Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var manifestStream = assembly.GetManifestResourceStream(ManifestResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ManifestResourceName}' not found.");
        using var doc = JsonDocument.Parse(manifestStream);
        var root = doc.RootElement;

        var entries = new List<Entry>();
        foreach (var p in root.GetProperty("packets").EnumerateArray())
        {
            var file = p.GetProperty("file").GetString()!;
            using var bodyStream = assembly.GetManifestResourceStream($"join_static/{file}")
                ?? throw new InvalidOperationException($"Embedded resource 'join_static/{file}' not found.");
            using var ms = new MemoryStream();
            bodyStream.CopyTo(ms);
            var body = ms.ToArray();

            var name = p.GetProperty("name").GetString()!;
            if (body.Length != p.GetProperty("bytes").GetInt32() ||
                !Convert.ToHexString(SHA256.HashData(body)).Equals(p.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Static join packet '{name}' ({file}) does not match its manifest.");
            entries.Add(new Entry(name, p.GetProperty("id").GetInt32(), body));
        }

        return (root.GetProperty("bedrock_version").GetString()!, root.GetProperty("protocol").GetInt32(), entries);
    }
}

/// <summary>The names under which <see cref="JoinStaticData"/> holds its packets (those of <c>bedrock-data join-static</c>).</summary>
static class JoinStaticNames
{
    public const string VoxelShapes = "voxel_shapes";
    public const string EntityProperties = "sync_entity_property";
    public const string EntityIdentifiers = "available_entity_identifiers";
    public const string PlayerFog = "player_fog";
    public const string CameraPresets = "camera_presets";
    public const string CameraAimAssistPresets = "camera_aim_assist_presets";
    public const string CameraSpline = "camera_spline";
    public const string TrimData = "trim_data";
}

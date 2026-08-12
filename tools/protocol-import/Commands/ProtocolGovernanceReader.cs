using System.Text.RegularExpressions;
using Zenith.ProtocolImport.Scaffolding;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal static class ProtocolGovernanceReader
{
    public static ProtocolGovernance Read(string protocol, string cache, ISchemaSource source, string? packetsDir)
    {
        var schemas = source.ListCachedPackets(cache).Select(name => source.ReadPacket(cache, name))
            .Where(packet => packet is not null).Cast<PacketSchema>().ToList();
        var root = RepoLocator.FindRoot(Directory.GetCurrentDirectory());
        packetsDir ??= root is null ? null : Path.Combine(root, "src", "zenith", "Packets");
        var generated = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var manual = 0;
        if (packetsDir is not null && Directory.Exists(packetsDir))
        {
            foreach (var path in Directory.GetFiles(packetsDir, "*.cs"))
            {
                if (File.ReadAllText(path).Contains("[GamePacket("))
                    generated[Path.GetFileNameWithoutExtension(path)] = ExistingPacketReader.ReadWirePropertyNames(path);
                else manual++;
            }
        }
        return ProtocolGovernanceAnalyzer.Analyze(protocol, schemas, generated, manual,
            SchemaCache.Validate(cache, source.Name));
    }

    public static string ReadProtocolVersion(string? configuredProtocol)
    {
        if (!string.IsNullOrWhiteSpace(configuredProtocol)) return configuredProtocol;
        var root = RepoLocator.FindRoot(Directory.GetCurrentDirectory());
        if (root is null) return "unknown";
        var identity = Path.Combine(root, "src", "zenith", "Server", "ServerIdentity.cs");
        if (!File.Exists(identity)) return "unknown";
        var match = Regex.Match(File.ReadAllText(identity), "VersionName\\s*=\\s*\\\"(?<version>[^\\\"]+)\\\"");
        return match.Success ? match.Groups["version"].Value : "unknown";
    }
}

using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Zenith.BedrockData.Commands;

/// <summary>
/// Imports a verified BDS/Endstone data bundle into the embedded assets (see README). No network, no server boot.
/// Checks version agreement, unresolved export errors, duplicate item names and that the imported component
/// definitions equal the captured registry's; it does not itself perform the capture roundtrip checks.
/// </summary>
internal sealed class ImportCommand : Command<ImportCommand.Settings>
{
    private static readonly string[] CapturedBodies = ["item_registry", "creative_content", "biome_definitions"];

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<source>")]
        [Description("Bundle directory (server/exported-blocks.json + capture/*).")]
        public required string Source { get; init; }

        [CommandOption("--output <DIR>")]
        [Description("Embedded data directory.")]
        [DefaultValue("src/zenith/data")]
        public string Output { get; init; } = "src/zenith/data";

        [CommandOption("--bedrock-version <VERSION>")]
        public required string BedrockVersion { get; init; }

        [CommandOption("--protocol <PROTOCOL>")]
        public required int Protocol { get; init; }

        [CommandOption("--bds-sha256 <HASH>")]
        public required string BdsSha256 { get; init; }

        [CommandOption("--endstone-version <VERSION>")]
        public required string EndstoneVersion { get; init; }
    }

    public override int Execute(CommandContext context, Settings s)
    {
        var export = Parse(Path.Combine(s.Source, "server", "exported-blocks.json"));
        var clientVersion = string.Join('.', s.BedrockVersion.Split('.').Take(3));
        var exportedVersion = (string)export["minecraft_version"]!;
        if (!exportedVersion.StartsWith("1.", StringComparison.Ordinal)) exportedVersion = "1." + exportedVersion;
        if (exportedVersion != clientVersion || export["errors"]!.AsArray().Count > 0)
            throw new InvalidDataException("Block export version differs or unresolved export errors remain.");

        var capture = Path.Combine(s.Source, "capture");
        var items = Parse(Path.Combine(capture, "item_palette.json"))["items"]!.AsArray();
        var creative = Parse(Path.Combine(capture, "creative_items.json"));
        if (items.Select(i => (string)i!["name"]!).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new InvalidDataException("Duplicate item names in the captured registry.");

        var captured = Parse(Path.Combine(capture, "item_registry-decoded.json"))["itemstates"]!.AsArray();
        var defined = captured.Where(i => HasValue(i!["nbt"]!["value"])).Select(i => (string)i!["name"]!).ToHashSet(StringComparer.Ordinal);
        var imported = items.Where(i => !string.IsNullOrEmpty((string?)i!["component_nbt_b64"]))
            .Select(i => (string)i!["name"]!).ToHashSet(StringComparer.Ordinal);
        if (!defined.SetEquals(imported))
            throw new InvalidDataException("The imported component definitions differ from the captured registry.");

        var blocks = export["blocks"]!.AsArray();
        var assets = new Dictionary<string, byte[]>
        {
            ["block_palette.nbt"] = BlockPaletteWriter.Write(blocks),
            ["item_palette.json"] = Text(new JsonObject { ["items"] = items.DeepClone() }),
            ["creative_items.json"] = Text(creative),
            ["biome_definitions.bin"] = File.ReadAllBytes(Path.Combine(capture, "biome_definitions.bin")),
        };
        if (assets.Values.Any(a => a.Length == 0)) throw new InvalidDataException("An imported asset is empty.");

        var manifest = new JsonObject
        {
            ["minecraft_version"] = clientVersion,
            ["protocol"] = s.Protocol,
            ["source"] = new JsonObject
            {
                ["bds_version"] = s.BedrockVersion,
                ["bds_url"] = $"https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-{s.BedrockVersion}.zip",
                ["bds_archive_sha256"] = s.BdsSha256,
                ["endstone_version"] = s.EndstoneVersion,
                ["block_candidates"] = "previous Zenith palette plus minecraft-data 3.117.0 block states; new types from the captured BDS ItemRegistry",
                ["block_validation"] = "each state and runtime ID queried from BDS via Endstone create_block_data; new state domains accepted only when returned unchanged",
            },
            ["counts"] = new JsonObject
            {
                ["block_states"] = blocks.Count,
                ["block_types"] = blocks.Select(b => (string)b!["name"]!).Distinct(StringComparer.Ordinal).Count(),
                ["items"] = items.Count,
                ["item_component_definitions"] = imported.Count,
                ["creative_groups"] = creative["groups"]!.AsArray().Count,
                ["creative_items"] = creative["items"]!.AsArray().Count,
                ["biomes"] = FirstVarUInt(assets["biome_definitions.bin"]),
            },
            ["assets"] = ToObject(assets.Select(a => KeyValuePair.Create(a.Key,
                (JsonNode)new JsonObject { ["bytes"] = a.Value.Length, ["sha256"] = Sha256(a.Value) }))),
            ["captured_packet_bodies"] = ToObject(CapturedBodies.Select(n =>
                KeyValuePair.Create(n, (JsonNode)Sha256(File.ReadAllBytes(Path.Combine(capture, n + ".bin")))))),
        };

        Directory.CreateDirectory(s.Output);
        foreach (var (name, payload) in assets) File.WriteAllBytes(Path.Combine(s.Output, name), payload);
        File.WriteAllBytes(Path.Combine(s.Output, "manifest.json"), Text(manifest));
        AnsiConsole.WriteLine(manifest["counts"]!.ToJsonString(Indented));
        return 0;
    }

    /// <summary>The captured item's component NBT is empty when its compound has no entries.</summary>
    private static bool HasValue(JsonNode? value) => value switch
    {
        null => false,
        JsonObject o => o.Count > 0,
        JsonArray a => a.Count > 0,
        _ => true,
    };

    private static JsonNode Parse(string path) => JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))!;

    private static byte[] Text(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString(Indented) + "\n");

    private static JsonObject ToObject(IEnumerable<KeyValuePair<string, JsonNode>> pairs)
    {
        var o = new JsonObject();
        foreach (var (k, v) in pairs) o[k] = v;
        return o;
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static int FirstVarUInt(byte[] payload)
    {
        var value = 0;
        for (var i = 0; i < Math.Min(5, payload.Length); i++)
        {
            value |= (payload[i] & 127) << (7 * i);
            if (payload[i] < 128) return value;
        }
        throw new InvalidDataException("Invalid leading biome array count.");
    }
}

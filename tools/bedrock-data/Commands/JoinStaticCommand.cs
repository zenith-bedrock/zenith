using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Zenith.BedrockData.Commands;

/// <summary>
/// Copies the join-time packets a stock BDS sends that are pure static data (entity identifiers, voxel shapes, trim data,
/// camera presets, ...) from a raw capture (<c>bds-reference/capture</c> index.json + one payload file per packet) into
/// the embedded assets, with a manifest that records the BDS version they came from. Nothing dynamic is imported: what
/// depends on the player or the world (player list, chunks, inventories, recipes unlocked) is not in the allow-list.
/// </summary>
internal sealed class JoinStaticCommand : Command<JoinStaticCommand.Settings>
{
    /// <summary>Packets that are the same for every player on a given build, in the order BDS sent them.</summary>
    internal static readonly string[] Allowed =
    [
        "voxel_shapes",
        "sync_entity_property",
        "available_entity_identifiers",
        "player_fog",
        "camera_presets",
        "camera_aim_assist_presets",
        "camera_spline",
        "trim_data",
    ];

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<capture>")]
        [Description("Directory with index.json and the payload .bin files (one per captured packet, header stripped).")]
        public required string Capture { get; init; }

        [CommandArgument(1, "<output>")]
        [Description("Directory that receives join_static/*.bin and join_static.json.")]
        public required string Output { get; init; }

        [CommandOption("--bedrock-version <VERSION>")]
        public required string BedrockVersion { get; init; }

        [CommandOption("--protocol <PROTOCOL>")]
        public required int Protocol { get; init; }
    }

    public override int Execute(CommandContext context, Settings s)
    {
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(s.Capture, "index.json")))!.AsArray();
        var dir = Path.Combine(s.Output, "join_static");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "*.bin")) File.Delete(old);

        var entries = new JsonArray();
        var order = 0;
        foreach (var packet in index)
        {
            var name = (string)packet!["name"]!;
            if (Array.IndexOf(Allowed, name) < 0) continue;

            var bytes = File.ReadAllBytes(Path.Combine(s.Capture, (string)packet["file"]!));
            if (bytes.Length != (int)packet["bytes"]!)
                throw new InvalidDataException($"{name}: capture index says {(int)packet["bytes"]!} bytes, file has {bytes.Length}.");

            var file = $"{order:D2}_{name}.bin";
            File.WriteAllBytes(Path.Combine(dir, file), bytes);
            entries.Add(new JsonObject
            {
                ["order"] = order,
                ["name"] = name,
                ["id"] = (int)packet["id"]!,
                ["file"] = file,
                ["bytes"] = bytes.Length,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            });
            order++;
        }

        foreach (var wanted in Allowed)
            if (!entries.Any(e => (string)e!["name"]! == wanted))
                throw new InvalidDataException($"The capture has no '{wanted}' packet.");

        var manifest = new JsonObject
        {
            ["source"] = "stock BDS join capture",
            ["bedrock_version"] = s.BedrockVersion,
            ["protocol"] = s.Protocol,
            ["packets"] = entries,
        };
        File.WriteAllText(Path.Combine(s.Output, "join_static.json"), manifest.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");

        AnsiConsole.MarkupLine($"[green]{entries.Count}[/] static join packets from BDS {Markup.Escape(s.BedrockVersion)} (protocol {s.Protocol}).");
        return 0;
    }
}

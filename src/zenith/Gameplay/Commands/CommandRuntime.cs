using Zenith.Player;
using Zenith.Diagnostics;
using System.Globalization;
using System.Diagnostics;

namespace Zenith.Gameplay.Commands;

/// <summary>Gameplay-facing command use cases. Parsing is protocol-free; mutations publish existing intents.</summary>
sealed class CommandRuntime
{
    private readonly PlayerManager _players;
    private readonly DiagnosticsInvestigation? _diagnostics;
    private readonly CommandCatalog _catalog = new();

    public CommandRuntime(PlayerManager players, DiagnosticsInvestigation? diagnostics = null)
    {
        _players = players;
        _diagnostics = diagnostics;
        var modes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["survival"] = GameMode.Survival, ["s"] = GameMode.Survival, ["0"] = GameMode.Survival,
            ["creative"] = GameMode.Creative, ["c"] = GameMode.Creative, ["1"] = GameMode.Creative
        };
        _catalog.Register(new CommandDefinition("gamemode", "Change game mode", CommandPermission.Any,
            [new CommandOverload(new EnumCommandArgument("mode", modes), new PlayerCommandArgument("target", name => _players.Get(name), Optional: true))], ["gm"]));
        _catalog.Register(new CommandDefinition("help", "List available commands", CommandPermission.Any,
            new CommandOverload(), new CommandOverload(new StringCommandArgument("command"))));
        _catalog.Register(new CommandDefinition("sample", "Show a bounded runtime sample request", CommandPermission.Any,
            new CommandOverload(), new CommandOverload(new IntegerCommandArgument("ticks", 1, 20))));
        var diagnosticsActions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["snapshot"] = "snapshot", ["compare"] = "compare", ["tick"] = "tick", ["incident"] = "incident"
        };
        _catalog.Register(new CommandDefinition("diagnostics", "Capture and compare runtime diagnostics", CommandPermission.Any,
            new CommandOverload(new EnumCommandArgument("action", diagnosticsActions))));

        var effectGive = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["give"] = "give" };
        var effectClear = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["clear"] = "clear" };
        var effectTypes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["poison"] = EffectType.Poison,
            ["regeneration"] = EffectType.Regeneration,
            ["regen"] = EffectType.Regeneration
        };
        _catalog.Register(new CommandDefinition("effect", "Apply or clear a timed player effect", CommandPermission.Any,
            [
                new CommandOverload(
                    new EnumCommandArgument("action", effectGive),
                    new EnumCommandArgument("type", effectTypes),
                    new IntegerCommandArgument("duration", 1, 24000, Optional: true),
                    new IntegerCommandArgument("amplifier", 0, 3, Optional: true),
                    new PlayerCommandArgument("target", name => _players.Get(name), Optional: true)),
                new CommandOverload(
                    new EnumCommandArgument("action", effectClear),
                    new PlayerCommandArgument("target", name => _players.Get(name), Optional: true))
            ]));
    }

    public IReadOnlyList<string> Suggest(string prefix) => _catalog.Suggest(prefix);
    internal CommandCatalog Catalog => _catalog;

    public CommandFeedback Execute(Player.Player source, string line)
    {
        var parsed = _catalog.Parse(line, CommandPermission.Any);
        if (parsed.Status == CommandParseStatus.Unknown) return new("Command", "Unknown command.");
        if (parsed.Status == CommandParseStatus.Denied) return new("Command", "You do not have permission.");
        if (parsed.Status == CommandParseStatus.Invalid) return new("Command", $"Usage: /{parsed.Definition!.Name}");

        var arguments = parsed.Arguments!;
        switch (parsed.Definition!.Name)
        {
            case "gamemode":
                var target = arguments.TryGetValue("target", out var targetValue) ? (Player.Player)targetValue : source;
                target.SubmitGameMode((GameMode)arguments["mode"]);
                return new("Game mode", $"Queued for {target.Username}.");
            case "help":
                return new("Commands", string.Join(", ", _catalog.Suggest("")));
            case "sample":
                return new("Command", arguments.TryGetValue("ticks", out var ticks) ? $"Sample request: {ticks} ticks." : "Sample request: default.");
            case "diagnostics":
                return ExecuteDiagnostics((string)arguments["action"]);
            case "effect":
                return ExecuteEffect(source, arguments);
            default:
                return new("Command", "Unknown command.");
        }
    }

    private CommandFeedback ExecuteEffect(Player.Player source, IReadOnlyDictionary<string, object> arguments)
    {
        var target = arguments.TryGetValue("target", out var targetValue) ? (Player.Player)targetValue : source;
        var action = (string)arguments["action"];
        if (action == "clear")
        {
            target.SubmitEffect(EffectIntent.Clear);
            return new("Effect", $"Clearing effects for {target.Username}.");
        }

        var type = (EffectType)arguments["type"];
        var duration = arguments.TryGetValue("duration", out var durationValue) ? (int)durationValue : 600;
        var amplifier = arguments.TryGetValue("amplifier", out var amplifierValue) ? (int)amplifierValue : 0;
        target.SubmitEffect(EffectIntent.Give(type, amplifier, duration));
        return new("Effect", $"Queued {type} (amplifier {amplifier}, {duration} ticks) for {target.Username}.");
    }

    private CommandFeedback ExecuteDiagnostics(string action)
    {
        if (_diagnostics is null) return new("Diagnostics", "Diagnostics are unavailable.");
        return action switch
        {
            "snapshot" => new("Diagnostics", DescribeSnapshot(_diagnostics.CaptureBaseline())),
            "compare" => _diagnostics.TryCompareBaseline(out var comparison)
                ? new("Diagnostics", comparison.ToConsole())
                : new("Diagnostics", "Capture a baseline first: /diagnostics snapshot"),
            "tick" => new("Diagnostics", DescribeTimings(_diagnostics.CaptureTopTimings())),
            "incident" => new("Diagnostics", DescribeLatestIncident(_diagnostics.CaptureRecentIncidents())),
            _ => new("Diagnostics", "Unknown diagnostics action.")
        };
    }

    private static string DescribeSnapshot(DiagnosticsSnapshot snapshot)
    {
        var tick = snapshot.Metrics.FirstOrDefault(metric => metric.Name == "tick");
        var milliseconds = tick.Count == 0 ? 0d : tick.TotalStopwatchTicks * 1000d / snapshot.StopwatchFrequency / tick.Count;
        return $"Baseline captured. tick.avg={milliseconds.ToString("F3", CultureInfo.InvariantCulture)}ms.";
    }

    private static string DescribeTimings(IReadOnlyList<DiagnosticsMetricSnapshot> timings) =>
        timings.Count == 0 ? "No timing samples yet." : string.Join(", ", timings.Select(timing =>
            $"{timing.Name}={timing.Value * 1000d / Stopwatch.Frequency:F3}ms"));

    private static string DescribeLatestIncident(IReadOnlyList<DiagnosticsSnapshot> incidents)
    {
        if (incidents.Count == 0) return "No incident snapshots captured yet.";
        var snapshot = incidents[^1];
        var tick = snapshot.Metrics.FirstOrDefault(metric => metric.Name == "tick");
        var actors = snapshot.Metrics.FirstOrDefault(metric => metric.Name == "gameplay.actors");
        var bytes = snapshot.Metrics.FirstOrDefault(metric => metric.Name == "network.packet-bytes.sent");
        var top = snapshot.Metrics.Where(metric => metric.Kind == DiagnosticMetricKind.Timing && metric.Parent == "tick")
            .OrderByDescending(metric => metric.Value).FirstOrDefault();
        var tickMs = tick.Value * 1000d / snapshot.StopwatchFrequency;
        var topMs = top.Value * 1000d / snapshot.StopwatchFrequency;
        return $"Incident samples={incidents.Count}; tick.last={tickMs:F3}ms; top={top.Name ?? "none"} {topMs:F3}ms; actors={actors.Value}; packetBytes.out={bytes.Value}.";
    }
}

readonly record struct CommandFeedback(string Title, string Message);

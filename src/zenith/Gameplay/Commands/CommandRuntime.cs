using Zenith.Player;
using Zenith.Gameplay.Permissions;
using Zenith.Diagnostics;
using System.Globalization;
using System.Diagnostics;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.Entities;
using Zenith.Rulebook;

namespace Zenith.Gameplay.Commands;

/// <summary>Gameplay-facing command use cases. Parsing is protocol-free; mutations publish existing intents.</summary>
sealed class CommandRuntime
{
    private readonly PlayerManager _players;
    private readonly DiagnosticsInvestigation? _diagnostics;
    private readonly GameRules? _rules;
    private readonly DifficultySetting? _difficulty;
    private readonly World.WeatherState? _weather;
    private readonly MobSpawner? _spawner;
    private readonly PermissionAuthority _authority;
    private readonly CommandCatalog _catalog = new();
    private readonly Dictionary<string, CommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="contributions">
    /// Commands added by modules during composition: each brings its grammar and its handler together, so adding one never touches
    /// this class. Registered with the built-ins before the catalog freezes; a clash with a built-in or another contribution throws.
    /// </param>
    public CommandRuntime(
        PlayerManager players, PermissionAuthority authority, DiagnosticsInvestigation? diagnostics = null,
        GameRules? rules = null, MobSpawner? spawner = null, IEnumerable<CommandBinding>? contributions = null,
        DifficultySetting? difficulty = null, World.WeatherState? weather = null)
    {
        _weather = weather;
        _difficulty = difficulty;
        _authority = authority;
        _spawner = spawner;
        _players = players;
        _rules = rules;
        _diagnostics = diagnostics;
        var modes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["survival"] = GameMode.Survival, ["s"] = GameMode.Survival, ["0"] = GameMode.Survival,
            ["creative"] = GameMode.Creative, ["c"] = GameMode.Creative, ["1"] = GameMode.Creative
        };
        Bind(ExecuteGameMode, new CommandDefinition("gamemode", "Change game mode", [PermissionNodes.GameModeSelf, PermissionNodes.GameModeOthers],
            [new CommandOverload(new EnumCommandArgument("mode", modes), new PlayerCommandArgument("target", ResolvePlayer, Optional: true))], ["gm"]));
        Bind(ExecuteHelp, new CommandDefinition("help", "List available commands", PermissionNodes.Help,
            new CommandOverload(), new CommandOverload(new StringCommandArgument("command"))));
        Bind(ExecuteSample, new CommandDefinition("sample", "Show a bounded runtime sample request", PermissionNodes.Sample,
            new CommandOverload(), new CommandOverload(new IntegerCommandArgument("ticks", 1, 20))));
        var diagnosticsActions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["snapshot"] = "snapshot", ["compare"] = "compare", ["tick"] = "tick", ["incident"] = "incident", ["window"] = "window"
        };
        Bind(ExecuteDiagnostics, new CommandDefinition("diagnostics", "Capture and compare runtime diagnostics", PermissionNodes.Diagnostics,
            new CommandOverload(new EnumCommandArgument("action", diagnosticsActions))));

        var effectGive = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["give"] = "give" };
        var effectClear = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["clear"] = "clear" };
        var effectTypes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["speed"] = EffectType.Speed,
            ["slowness"] = EffectType.Slowness,
            ["haste"] = EffectType.Haste,
            ["mining_fatigue"] = EffectType.MiningFatigue,
            ["strength"] = EffectType.Strength,
            ["instant_health"] = EffectType.InstantHealth,
            ["instant_damage"] = EffectType.InstantDamage,
            ["jump_boost"] = EffectType.JumpBoost,
            ["nausea"] = EffectType.Nausea,
            ["resistance"] = EffectType.Resistance,
            ["fire_resistance"] = EffectType.FireResistance,
            ["water_breathing"] = EffectType.WaterBreathing,
            ["invisibility"] = EffectType.Invisibility,
            ["blindness"] = EffectType.Blindness,
            ["night_vision"] = EffectType.NightVision,
            ["hunger"] = EffectType.Hunger,
            ["weakness"] = EffectType.Weakness,
            ["wither"] = EffectType.Wither,
            ["saturation"] = EffectType.Saturation,
            ["levitation"] = EffectType.Levitation,
            ["poison"] = EffectType.Poison,
            ["regeneration"] = EffectType.Regeneration,
            ["regen"] = EffectType.Regeneration
        };
        Bind(ExecuteEffect, new CommandDefinition("effect", "Apply or clear a timed player effect", [PermissionNodes.EffectSelf, PermissionNodes.EffectOthers],
            [
                new CommandOverload(
                    new EnumCommandArgument("action", effectGive),
                    new EnumCommandArgument("type", effectTypes),
                    new IntegerCommandArgument("duration", 1, 24000, Optional: true),
                    new IntegerCommandArgument("amplifier", 0, 3, Optional: true),
                    new PlayerCommandArgument("target", ResolvePlayer, Optional: true)),
                new CommandOverload(
                    new EnumCommandArgument("action", effectClear),
                    new PlayerCommandArgument("target", ResolvePlayer, Optional: true))
            ], []));

        if (rules is not null)
        {
            var names = rules.Snapshot.ToDictionary(rule => rule.Name, rule => (object)rule.Name, StringComparer.OrdinalIgnoreCase);
            Bind(ExecuteGameRule, new CommandDefinition("gamerule", "Show or change a game rule", [PermissionNodes.GameRuleRead, PermissionNodes.GameRuleWrite],
                [new CommandOverload(new EnumCommandArgument("rule", names), new StringCommandArgument("value", Optional: true))], []));
        }

        if (difficulty is not null)
        {
            var levels = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["peaceful"] = Difficulty.Peaceful, ["easy"] = Difficulty.Easy, ["normal"] = Difficulty.Normal, ["hard"] = Difficulty.Hard
            };
            Bind(ExecuteDifficulty, new CommandDefinition("difficulty", "Show or change the world difficulty", [PermissionNodes.DifficultyRead, PermissionNodes.DifficultyWrite],
                [new CommandOverload(new EnumCommandArgument("level", levels, Optional: true))], []));
        }

        if (weather is not null)
        {
            var kinds = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["query"] = -1, ["clear"] = 0, ["rain"] = 1, ["thunder"] = 2
            };
            Bind(ExecuteWeather, new CommandDefinition("weather", "Show or change the weather", PermissionNodes.Weather,
                new CommandOverload(new EnumCommandArgument("kind", kinds), new IntegerCommandArgument("duration", 1, 1000000, Optional: true))));
        }

        if (spawner is not null)
        {
            var species = spawner.Names.ToDictionary(name => name, name => (object)name, StringComparer.OrdinalIgnoreCase);
            Bind(ExecuteSummon, new CommandDefinition("summon", "Summon a mob at your position", PermissionNodes.Summon,
                new CommandOverload(new EnumCommandArgument("mob", species))));
        }

        foreach (var contribution in contributions ?? [])
            Bind(contribution);

        _catalog.Freeze();
    }

    private void Bind(CommandHandler handler, CommandDefinition definition) => Bind(new CommandBinding(definition, handler));

    private void Bind(CommandBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(binding.Handler);
        // Registering validates the whole definition first, so a rejected binding leaves neither catalog nor handlers changed.
        _catalog.Register(binding.Definition);
        _handlers.Add(binding.Definition.Name, binding.Handler);
    }

    private object? ResolvePlayer(string name) => _players.Get(name) is { } player ? (object)new PlayerRef(player.Uuid, player.Username, player.RuntimeId) : null;

    /// <summary>The command names and aliases the player may use, so discovery shows exactly what execution would allow.</summary>
    public IReadOnlyList<string> Suggest(Player.Player source, string prefix) => _catalog.Suggest(prefix, HoldsFor(source));

    internal CommandCatalog Catalog => _catalog;

    private Func<string, bool> HoldsFor(Player.Player source)
    {
        var principal = source.Principal;
        return node => _authority.Has(principal, node);
    }

    private const string NoPermission = "You do not have permission.";

    private static CommandFeedback Rejected(string title, string message) => new(title, message, CommandOutcome.Rejected);
    private static CommandFeedback Accepted(string title, string message) => new(title, message, CommandOutcome.Accepted);
    private static CommandFeedback Applied(string title, string message) => new(title, message, CommandOutcome.Applied);

    public CommandFeedback Execute(Player.Player source, string line)
    {
        // Both entrances (chat slash and CommandRequest) land here, so the same authority applies whatever the client sent.
        var parsed = _catalog.Parse(line, HoldsFor(source));
        if (parsed.Status == CommandParseStatus.Unknown) return Rejected("Command", "Unknown command.");
        if (parsed.Status == CommandParseStatus.Denied) return Rejected("Command", NoPermission);
        if (parsed.Status == CommandParseStatus.Invalid) return Rejected("Command", $"Usage: /{parsed.Definition!.Name}");

        var invocation = new CommandInvocation(
            source.Principal, source.Username, source.PositionX, source.PositionY, source.PositionZ, parsed.Arguments!);
        return _handlers[parsed.Definition!.Name](invocation);
    }

    /// <summary>The live player behind a by-value reference, or null once they are gone or the reference is stale.</summary>
    private Player.Player? Find(long sessionId, Guid id) =>
        _players.GetByRuntimeId(sessionId) is { } player && player.Uuid == id ? player : null;

    private CommandFeedback ExecuteHelp(in CommandInvocation invocation)
    {
        var caller = invocation.Caller;
        return Applied("Commands", string.Join(", ", _catalog.Suggest("", node => _authority.Has(caller, node))));
    }

    private static CommandFeedback ExecuteSample(in CommandInvocation invocation) =>
        Applied("Command", invocation.Arguments.TryGetValue("ticks", out var ticks) ? $"Sample request: {ticks} ticks." : "Sample request: default.");

    private CommandFeedback ExecuteDiagnostics(in CommandInvocation invocation) =>
        ExecuteDiagnostics((string)invocation.Arguments["action"]);

    /// <summary>Resolves a command's target: the named player, or the caller when none was given. Null when they are not (or no longer) online.</summary>
    private Player.Player? ResolveTarget(in CommandInvocation invocation)
    {
        if (invocation.Arguments.TryGetValue("target", out var value))
        {
            var named = (PlayerRef)value;
            return Find(named.SessionId, named.Id);
        }

        return Find(invocation.Caller.SessionId, invocation.Caller.Subject);
    }

    /// <summary>The node a command needs to act on <paramref name="target"/>, and whether the sender holds it. Self and others are separate grants.</summary>
    private bool TryAuthorizeTarget(in CommandInvocation invocation, Player.Player target, string selfNode, string othersNode, out Authorization issued)
    {
        issued = new Authorization(invocation.Caller, target.RuntimeId == invocation.Caller.SessionId ? selfNode : othersNode);
        return _authority.Has(issued.Principal, issued.Node);
    }

    private CommandFeedback ExecuteGameMode(in CommandInvocation invocation)
    {
        if (ResolveTarget(invocation) is not { } target) return Rejected("Game mode", "That player is not online.");
        if (!TryAuthorizeTarget(invocation, target, PermissionNodes.GameModeSelf, PermissionNodes.GameModeOthers, out var issued))
            return Rejected("Game mode", NoPermission);
        target.Intents.SubmitGameMode((GameMode)invocation.Arguments["mode"], issued);
        return Accepted("Game mode", $"Queued for {target.Username}.");
    }

    private CommandFeedback ExecuteSummon(in CommandInvocation invocation)
    {
        var mob = (string)invocation.Arguments["mob"];
        var issued = new Authorization(invocation.Caller, PermissionNodes.Summon);
        // Accepted, not applied: the spawn happens on the next tick, and only if the authority still holds then.
        return _spawner!.Request(mob, invocation.X, invocation.Y, invocation.Z, issued)
            ? Accepted("Summon", $"Queued {mob}.")
            : Rejected("Summon", $"Unknown mob '{mob}'.");
    }

    private CommandFeedback ExecuteWeather(in CommandInvocation invocation)
    {
        var kind = (int)invocation.Arguments["kind"];
        if (kind < 0)
            return Applied("Weather", _weather!.Level switch { 0 => "The sky is clear.", 1 => "It is raining.", _ => "There is a thunderstorm." });

        var duration = invocation.Arguments.TryGetValue("duration", out var value) ? (int)value : World.WeatherState.DefaultCommandTicks;
        _weather!.Set(kind, duration);
        return Applied("Weather", kind switch { 0 => "The sky clears.", 1 => "It starts to rain.", _ => "A thunderstorm starts." });
    }

    private CommandFeedback ExecuteDifficulty(in CommandInvocation invocation)
    {
        if (!invocation.Arguments.TryGetValue("level", out var level))
            return _authority.Has(invocation.Caller, PermissionNodes.DifficultyRead)
                ? Applied("Difficulty", $"The difficulty is {_difficulty!.Value}.")
                : Rejected("Difficulty", NoPermission);

        if (!_authority.Has(invocation.Caller, PermissionNodes.DifficultyWrite)) return Rejected("Difficulty", NoPermission);
        _difficulty!.Set((Difficulty)level);
        return Applied("Difficulty", $"The difficulty is now {_difficulty.Value}.");
    }

    private CommandFeedback ExecuteGameRule(in CommandInvocation invocation)
    {
        var name = (string)invocation.Arguments["rule"];
        var current = _rules!.Get(name);
        if (!invocation.Arguments.TryGetValue("value", out var value))
        {
            return _authority.Has(invocation.Caller, PermissionNodes.GameRuleRead)
                ? Applied("Game rule", $"{current.Name} = {current.ValueText}")
                : Rejected("Game rule", NoPermission);
        }

        if (!_authority.Has(invocation.Caller, PermissionNodes.GameRuleWrite)) return Rejected("Game rule", NoPermission);
        if (!_rules.TrySet(current.Name, (string)value, out var error))
            return Rejected("Game rule", error);
        return Applied("Game rule", $"{current.Name} = {_rules.Get(current.Name).ValueText}");
    }

    private CommandFeedback ExecuteEffect(in CommandInvocation invocation)
    {
        if (ResolveTarget(invocation) is not { } target) return Rejected("Effect", "That player is not online.");
        if (!TryAuthorizeTarget(invocation, target, PermissionNodes.EffectSelf, PermissionNodes.EffectOthers, out var issued))
            return Rejected("Effect", NoPermission);
        var arguments = invocation.Arguments;
        var action = (string)arguments["action"];
        if (action == "clear")
        {
            target.Effects.SubmitEffect(EffectIntent.ClearBy(issued));
            return Accepted("Effect", $"Clearing effects for {target.Username}.");
        }

        var type = (EffectType)arguments["type"];
        var duration = arguments.TryGetValue("duration", out var durationValue) ? (int)durationValue : 600;
        var amplifier = arguments.TryGetValue("amplifier", out var amplifierValue) ? (int)amplifierValue : 0;
        target.Effects.SubmitEffect(EffectIntent.Give(type, amplifier, duration, issued));
        return Accepted("Effect", $"Queued {type} (amplifier {amplifier}, {duration} ticks) for {target.Username}.");
    }

    private CommandFeedback ExecuteDiagnostics(string action)
    {
        if (_diagnostics is null) return Rejected("Diagnostics", "Diagnostics are unavailable.");
        return action switch
        {
            "snapshot" => Applied("Diagnostics", DescribeSnapshot(_diagnostics.CaptureBaseline())),
            "compare" => _diagnostics.TryCompareBaseline(out var comparison)
                ? Applied("Diagnostics", comparison.ToConsole())
                : Rejected("Diagnostics", "Capture a baseline first: /diagnostics snapshot"),
            "tick" => Applied("Diagnostics", DescribeTimings(_diagnostics.CaptureTopTimings())),
            "window" => Applied("Diagnostics", _diagnostics.CaptureWindow().ToConsole()),
            "incident" => Applied("Diagnostics", DescribeLatestIncident(_diagnostics.CaptureRecentIncidents())),
            _ => Rejected("Diagnostics", "Unknown diagnostics action.")
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

